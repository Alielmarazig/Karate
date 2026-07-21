using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Karate.Services;

/// <summary>
/// Stage 2 of winget independence: installs a package straight from the winget
/// repository manifest — download the installer, verify its SHA-256 against the
/// manifest (winget's own security model), run it silently. Used only when the
/// winget client is not on the machine.
/// </summary>
public static class WingetManifestInstaller
{
    private const string CdnBase = "https://cdn.winget.microsoft.com/cache/";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Karate-UpdateMonitor");
        return client;
    }

    public static async Task<(bool Success, string Message)> InstallAsync(
        string packageId, string version, IProgress<double>? progress = null)
    {
        var path = WingetIndexService.GetManifestPath(packageId, version);
        if (path is null)
            return (false, "manifest not found in the package index");

        string yaml;
        try
        {
            yaml = await Http.GetStringAsync(CdnBase + path);
        }
        catch
        {
            return (false, "could not download the package manifest");
        }

        var installer = PickInstaller(yaml);
        if (installer is null)
            return (false, "no suitable installer in the manifest");
        var (url, sha256, type, silentArgs) = installer.Value;

        if (type.Equals("msstore", StringComparison.OrdinalIgnoreCase))
            return (false, "Store package — update it via the Microsoft Store");
        if (type.Equals("portable", StringComparison.OrdinalIgnoreCase))
            return (false, "portable package — download it manually");

        string file;
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "Karate");
            Directory.CreateDirectory(dir);
            file = Path.Combine(dir, Path.GetFileName(new Uri(url).LocalPath));

            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? -1L;
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var target = File.Create(file))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    if (total > 0)
                        progress?.Report(done * 100.0 / total);
                }
            }
        }
        catch
        {
            return (false, "installer download failed");
        }

        // Trust gate: the hash must match the manifest, exactly like winget verifies.
        await using (var stream = File.OpenRead(file))
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
            if (!hash.Equals(sha256, StringComparison.OrdinalIgnoreCase))
                return (false, "SHA-256 mismatch — download rejected");
        }

        var args = silentArgs ?? type.ToLowerInvariant() switch
        {
            "msi" or "wix" => "/qn /norestart",
            "inno" => "/VERYSILENT /NORESTART",
            "nullsoft" => "/S",
            "burn" => "/quiet /norestart",
            _ => "",
        };

        var psi = file.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo("msiexec.exe", $"/i \"{file}\" {args}".TrimEnd())
            : new ProcessStartInfo(file, args);
        psi.UseShellExecute = true; // lets the installer raise UAC itself

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return (false, "failed to start the installer");
            await process.WaitForExitAsync();
            return process.ExitCode is 0 or 3010
                ? (true, process.ExitCode == 3010 ? "installed — reboot required" : "installed")
                : (false, $"installer exit code 0x{process.ExitCode:X8}");
        }
        catch (Win32Exception)
        {
            return (false, "cancelled at the UAC prompt");
        }
    }

    /// <summary>Picks the best installer entry from a merged manifest: x64 first, then x86, then anything.</summary>
    internal static (string Url, string Sha256, string Type, string? Silent)? PickInstaller(string yaml)
    {
        var topType = Regex.Match(yaml, @"(?m)^InstallerType:\s*(\S+)").Groups[1].Value;
        var topSilent = Regex.Match(yaml, @"(?m)^\s*Silent:\s*(.+)$").Groups[1].Value.Trim();

        var sectionMatch = Regex.Match(yaml, @"(?m)^Installers:\s*$");
        if (!sectionMatch.Success)
            return null;
        var section = yaml[(sectionMatch.Index + sectionMatch.Length)..];
        var nextTopKey = Regex.Match(section, @"(?m)^[A-Za-z]");
        if (nextTopKey.Success)
            section = section[..nextTopKey.Index];

        var entries = new List<Dictionary<string, string>>();
        foreach (var block in Regex.Split(section, @"(?m)^-\s"))
        {
            if (block.Trim().Length == 0)
                continue;
            var entry = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match kv in Regex.Matches(block, @"(?m)^\s*([A-Za-z0-9]+):\s*(.+?)\s*$"))
            {
                var key = kv.Groups[1].Value;
                if (!entry.ContainsKey(key))
                    entry[key] = kv.Groups[2].Value.Trim().Trim('"', '\'');
            }
            if (entry.Count > 0)
                entries.Add(entry);
        }

        var pick = entries.FirstOrDefault(e => Arch(e) == "x64")
                ?? entries.FirstOrDefault(e => Arch(e) == "x86")
                ?? entries.FirstOrDefault();
        if (pick is null
            || !pick.TryGetValue("InstallerUrl", out var url)
            || !pick.TryGetValue("InstallerSha256", out var sha))
            return null;

        var type = pick.GetValueOrDefault("InstallerType", topType);
        var silent = pick.GetValueOrDefault("Silent") ?? (topSilent.Length > 0 ? topSilent : null);
        return (url, sha, type, silent);

        static string Arch(Dictionary<string, string> e) =>
            e.GetValueOrDefault("Architecture", "").ToLowerInvariant();
    }
}
