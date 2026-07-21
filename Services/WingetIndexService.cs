using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using Karate.Models;
using Microsoft.Data.Sqlite;

namespace Karate.Services;

public record IndexUpgrade(InstalledApp App, string PackageId, string LatestVersion);

/// <summary>
/// Winget-free update detection: downloads the winget community repository's
/// pre-built SQLite index (the same source.msix the winget client uses) and
/// matches installed apps against it locally. No winget client required.
/// </summary>
public static class WingetIndexService
{
    private const string SourceUrl = "https://cdn.winget.microsoft.com/cache/source.msix";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Karate-UpdateMonitor");
        return client;
    }

    private static string CacheDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Karate", "winget-index");

    public static string DbPath => Path.Combine(CacheDir, "index.db");

    /// <summary>Downloads/refreshes the package index (kept for 12 h). Returns true when a usable index exists.</summary>
    public static async Task<bool> EnsureIndexAsync(IProgress<double>? progress = null, bool force = false)
    {
        try
        {
            if (!force && File.Exists(DbPath)
                && DateTime.UtcNow - File.GetLastWriteTimeUtc(DbPath) < TimeSpan.FromHours(12))
                return true;

            Directory.CreateDirectory(CacheDir);
            var msixPath = Path.Combine(CacheDir, "source.msix");

            using (var response = await Http.GetAsync(SourceUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? -1L;
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var file = File.Create(msixPath);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    if (total > 0)
                        progress?.Report(done * 100.0 / total);
                }
            }

            // An .msix is a zip; the index lives at Public/index.db.
            using (var zip = ZipFile.OpenRead(msixPath))
            {
                var entry = zip.GetEntry("Public/index.db")
                    ?? throw new InvalidDataException("index.db missing from source.msix");
                var tmp = DbPath + ".tmp";
                entry.ExtractToFile(tmp, overwrite: true);
                File.Move(tmp, DbPath, overwrite: true);
            }
            File.Delete(msixPath);
            return true;
        }
        catch
        {
            return File.Exists(DbPath); // a stale index is still useful
        }
    }

    /// <summary>Matches installed apps against the index. Null = index unusable (caller should fall back).</summary>
    public static List<IndexUpgrade>? FindUpgrades(IReadOnlyList<InstalledApp> apps)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly;Pooling=False");
            conn.Open();
            var results = new List<IndexUpgrade>();
            foreach (var app in apps)
            {
                var match = MatchApp(conn, app);
                if (match is not null)
                    results.Add(match);
            }
            return results;
        }
        catch
        {
            return null;
        }
    }

    private static IndexUpgrade? MatchApp(SqliteConnection conn, InstalledApp app)
    {
        var (normName, arch) = NormalizeName(app.Name);
        if (normName.Length < 3)
            return null;

        var candidates = new List<string>();
        if (arch.Length > 0)
            candidates.Add($"{normName}({arch})"); // winget stores arch-qualified names as name(X64)
        candidates.Add(normName);

        var normPublisher = NormalizePublisher(app.Publisher);

        foreach (var candidate in candidates)
        {
            var ids = QueryCandidateIds(conn, candidate);
            if (ids.Count == 0)
                continue;

            string? chosen = null;
            if (ids.Count == 1)
            {
                chosen = ids[0];
                // Same name, different vendor → probably a different product entirely.
                if (normPublisher.Length > 0 && !PublisherMatches(conn, chosen, normPublisher))
                    chosen = null;
            }
            else if (normPublisher.Length > 0)
            {
                var byPublisher = ids.Where(id => PublisherMatches(conn, id, normPublisher)).ToList();
                if (byPublisher.Count == 1)
                    chosen = byPublisher[0];
            }

            if (chosen is null)
                continue;

            var latest = LatestVersion(conn, chosen);
            if (latest is null)
                return null;
            return CompareVersions(latest, app.Version) > 0
                ? new IndexUpgrade(app, chosen, latest)
                : null; // matched and up to date
        }
        return null;
    }

    private static List<string> QueryCandidateIds(SqliteConnection conn, string normName)
    {
        var list = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT i.id FROM norm_names nn
            JOIN norm_names_map nm ON nm.norm_name = nn.rowid
            JOIN manifest m ON m.rowid = nm.manifest
            JOIN ids i ON i.rowid = m.id
            WHERE nn.norm_name = @n COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("@n", normName);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetString(0));
        return list;
    }

    private static bool PublisherMatches(SqliteConnection conn, string packageId, string normPublisher)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM norm_publishers np
            JOIN norm_publishers_map pm ON pm.norm_publisher = np.rowid
            JOIN manifest m ON m.rowid = pm.manifest
            JOIN ids i ON i.rowid = m.id
            WHERE i.id = @id AND np.norm_publisher = @p COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("@id", packageId);
        cmd.Parameters.AddWithValue("@p", normPublisher);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static string? LatestVersion(SqliteConnection conn, string packageId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT v.version FROM manifest m
            JOIN ids i ON i.rowid = m.id
            JOIN versions v ON v.rowid = m.version
            WHERE i.id = @id
            """;
        cmd.Parameters.AddWithValue("@id", packageId);
        string? best = null;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var version = reader.GetString(0).Trim();
            if (version.Length == 0
                || version.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                || version.Equals("latest", StringComparison.OrdinalIgnoreCase))
                continue;
            if (best is null || CompareVersions(version, best) > 0)
                best = version;
        }
        return best;
    }

    /// <summary>Resolves the CDN-relative manifest path for a specific package version (Stage 2 installs).</summary>
    public static string? GetManifestPath(string packageId, string version)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly;Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                WITH RECURSIVE walk(parent, path) AS (
                    SELECT p.parent, p.pathpart FROM manifest m
                    JOIN pathparts p ON p.rowid = m.pathpart
                    JOIN ids i ON i.rowid = m.id
                    JOIN versions v ON v.rowid = m.version
                    WHERE i.id = @id AND v.version = @v
                    UNION ALL
                    SELECT pp.parent, pp.pathpart || '/' || walk.path FROM walk
                    JOIN pathparts pp ON pp.rowid = walk.parent
                )
                SELECT path FROM walk WHERE parent IS NULL OR parent = '' LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@id", packageId);
            cmd.Parameters.AddWithValue("@v", version);
            return cmd.ExecuteScalar() as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Approximation of winget's ARP name normalization. Returns (normalized, arch).</summary>
    internal static (string Name, string Arch) NormalizeName(string displayName)
    {
        var s = displayName.ToLowerInvariant();
        var arch = "";
        foreach (var (token, canonical) in new[]
        {
            ("arm64", "arm64"), ("aarch64", "arm64"),
            ("amd64", "x64"), ("x64", "x64"), ("64-bit", "x64"), ("64 bit", "x64"),
            ("x86", "x86"), ("32-bit", "x86"), ("32 bit", "x86"), ("i386", "x86"),
        })
        {
            if (s.Contains(token))
            {
                arch = canonical;
                s = s.Replace(token, " ");
                break;
            }
        }
        s = Regex.Replace(s, @"\([^)]*\)", " ");                 // parenthesized qualifiers
        s = Regex.Replace(s, @"\b(version|release|edition)\b", " ");
        s = Regex.Replace(s, @"\bv?\d+(?:[._\-]\d+)+\b", " ");   // version tokens (24.09, 1.2.3.4)
        s = Regex.Replace(s, @"[^\p{L}\p{Nd}]+", "");            // letters + digits only
        return (s, arch);
    }

    /// <summary>Publisher normalization: lowercase alphanumerics, legal suffixes stripped.</summary>
    internal static string NormalizePublisher(string publisher)
    {
        var s = Regex.Replace(publisher.ToLowerInvariant(), @"[^\p{L}\p{Nd}\s]+", " ");
        var stop = new HashSet<string>
        {
            "inc", "incorporated", "llc", "ltd", "limited", "corp", "corporation",
            "co", "gmbh", "ag", "sa", "srl", "sarl", "bv", "ab", "oy", "plc", "pty", "se", "kg", "sro",
        };
        var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (tokens.Count > 1 && stop.Contains(tokens[^1]))
            tokens.RemoveAt(tokens.Count - 1);
        return string.Concat(tokens);
    }

    /// <summary>Segment-wise, numeric-aware version comparison ("1.21.8b" &gt; "1.9b").</summary>
    public static int CompareVersions(string a, string b)
    {
        var pa = Tokenize(a);
        var pb = Tokenize(b);
        for (int i = 0; i < Math.Max(pa.Count, pb.Count); i++)
        {
            var c = CompareSegment(i < pa.Count ? pa[i] : "0", i < pb.Count ? pb[i] : "0");
            if (c != 0)
                return c;
        }
        return 0;

        static List<string> Tokenize(string v) =>
            v.Trim().TrimStart('v', 'V')
             .Split('.', '-', '+', '_')
             .Select(t => t.Trim())
             .Where(t => t.Length > 0)
             .ToList();

        // Numeric prefix decides first ("21" vs "9b" → 21 > 9). On numeric ties,
        // semver convention: a bare number outranks one with a trailing tag
        // ("21" > "21b"), mirroring winget's 1.0 > 1.0-beta behavior.
        static int CompareSegment(string x, string y)
        {
            var (nx, rx) = SplitNumericPrefix(x);
            var (ny, ry) = SplitNumericPrefix(y);
            if (nx != ny)
                return nx.CompareTo(ny);
            if (rx.Length == 0 && ry.Length > 0)
                return 1;
            if (rx.Length > 0 && ry.Length == 0)
                return -1;
            return string.Compare(rx, ry, StringComparison.OrdinalIgnoreCase);
        }

        static (long Number, string Suffix) SplitNumericPrefix(string s)
        {
            int i = 0;
            while (i < s.Length && char.IsAsciiDigit(s[i]))
                i++;
            if (i == 0)
                return (-1, s); // pure text sorts below any number
            var digits = i > 18 ? s[..18] : s[..i]; // overflow guard
            return (long.Parse(digits), s[i..]);
        }
    }
}
