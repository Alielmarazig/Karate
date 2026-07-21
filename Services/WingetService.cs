using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Karate.Services;

public record WingetUpgrade(string Name, string Id, string CurrentVersion, string AvailableVersion);

public static class WingetService
{
    /// <summary>
    /// Runs `winget upgrade` and parses its table output.
    /// Returns null when winget is not available on this system.
    /// </summary>
    public static async Task<List<WingetUpgrade>?> GetUpgradesAsync()
    {
        string output;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "upgrade --include-unknown --accept-source-agreements --disable-interactivity",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };

            using var process = Process.Start(psi);
            if (process is null)
                return null;

            output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
        }
        catch (Win32Exception)
        {
            return null;
        }

        return ParseUpgradeOutput(output);
    }

    /// <summary>
    /// Upgrades a single package by exact winget id. Installers may still pop a
    /// UAC prompt — that is expected and stays in the user's hands.
    /// </summary>
    public static async Task<(bool Success, int ExitCode)> UpgradeAsync(string wingetId)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = $"upgrade --id \"{wingetId}\" --exact --silent " +
                            "--accept-package-agreements --accept-source-agreements --disable-interactivity",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };

            using var process = Process.Start(psi);
            if (process is null)
                return (false, -1);

            // Drain streams so the process can't block on a full pipe buffer.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);

            return (process.ExitCode == 0, process.ExitCode);
        }
        catch (Win32Exception)
        {
            return (false, -1);
        }
    }

    internal static List<WingetUpgrade> ParseUpgradeOutput(string output)
    {
        var upgrades = new List<WingetUpgrade>();
        var lines = output.Replace("\r", "").Split('\n');

        // Locate the table header to derive column offsets.
        int headerIndex = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("Name") && line.Contains(" Id") && line.Contains("Version") && line.Contains("Available"))
            {
                headerIndex = i;
                break;
            }
        }

        if (headerIndex < 0)
            return upgrades;

        var header = lines[headerIndex];
        int idCol = header.IndexOf(" Id", StringComparison.Ordinal) + 1;
        int versionCol = header.IndexOf("Version", StringComparison.Ordinal);
        int availableCol = header.IndexOf("Available", StringComparison.Ordinal);
        int sourceCol = header.IndexOf("Source", StringComparison.Ordinal);

        for (int i = headerIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (line.TrimStart().StartsWith('-'))
                continue;
            // Trailing summary such as "12 upgrades available."
            if (char.IsDigit(line.TrimStart().FirstOrDefault()) && line.Contains("upgrade", StringComparison.OrdinalIgnoreCase))
                continue;
            if (line.Length < availableCol)
                continue;

            var padded = line.PadRight(sourceCol > 0 ? sourceCol : line.Length);
            var name = Slice(padded, 0, idCol);
            var id = Slice(padded, idCol, versionCol);
            var version = Slice(padded, versionCol, availableCol);
            var available = Slice(padded, availableCol, sourceCol > availableCol ? sourceCol : padded.Length);

            // Skip repeated headers from secondary tables ("require explicit targeting").
            if (name is "Name" || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(available))
                continue;

            upgrades.Add(new WingetUpgrade(name, id, version, available));
        }

        return upgrades;
    }

    private static string Slice(string line, int start, int end)
    {
        if (start >= line.Length)
            return "";
        end = Math.Min(end, line.Length);
        if (end <= start)
            return "";
        return line[start..end].Trim().TrimEnd('…');
    }
}
