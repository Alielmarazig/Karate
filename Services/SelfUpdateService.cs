using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace Karate.Services;

public record ReleaseInfo(Version Version, string TagName, string MsiUrl, string ExeUrl, string HtmlUrl);

/// <summary>
/// Keeps Karate itself fresh: checks the GitHub release feed on startup and
/// applies the newer MSI (installed copies) or swaps the portable exe.
/// </summary>
public static class SelfUpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/Alielmarazig/Karate/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Karate-UpdateMonitor");
        return client;
    }

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Installed under Program Files → update via MSI; anywhere else → portable swap.</summary>
    public static bool IsInstalledCopy =>
        AppContext.BaseDirectory.Contains("Program Files", StringComparison.OrdinalIgnoreCase);

    public static async Task<ReleaseInfo?> CheckAsync()
    {
        try
        {
            var json = await Http.GetStringAsync(LatestReleaseApi);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
                return null;

            string msi = "", exe = "";
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                if (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                    msi = url;
                else if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                         && name.Contains("portable", StringComparison.OrdinalIgnoreCase))
                    exe = url;
            }

            return new ReleaseInfo(version, tag, msi, exe,
                root.GetProperty("html_url").GetString() ?? "");
        }
        catch
        {
            // Offline, rate-limited, or no releases — never bother the user.
            return null;
        }
    }

    public static async Task<string> DownloadAsync(string url, string fileName)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Karate");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        var bytes = await Http.GetByteArrayAsync(url);
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    /// <summary>Waits for this process to exit, installs the MSI, relaunches. Shuts the app down.</summary>
    public static void ApplyMsiAndRestart(string msiPath)
    {
        var relaunch = Path.Combine(AppContext.BaseDirectory, "Karate.exe");
        RunDetachedBatch($"""
            @echo off
            :loop
            tasklist /FI "PID eq {Environment.ProcessId}" 2>NUL | find "{Environment.ProcessId}" >NUL
            if not errorlevel 1 (
                timeout /T 1 /NOBREAK >NUL
                goto loop
            )
            msiexec /i "{msiPath}" /passive /norestart
            start "" "{relaunch}"
            del "%~f0"
            """);
    }

    /// <summary>Waits for this process to exit, swaps the portable exe, relaunches. Shuts the app down.</summary>
    public static void ApplyPortableAndRestart(string newExePath)
    {
        var target = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Karate.exe");
        RunDetachedBatch($"""
            @echo off
            :loop
            tasklist /FI "PID eq {Environment.ProcessId}" 2>NUL | find "{Environment.ProcessId}" >NUL
            if not errorlevel 1 (
                timeout /T 1 /NOBREAK >NUL
                goto loop
            )
            copy /Y "{newExePath}" "{target}" >NUL
            start "" "{target}"
            del "%~f0"
            """);
    }

    private static void RunDetachedBatch(string script)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Karate");
        Directory.CreateDirectory(dir);
        var bat = Path.Combine(dir, "apply-update.bat");
        File.WriteAllText(bat, script);

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{bat}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
        });

        Application.Current.Shutdown();
    }
}
