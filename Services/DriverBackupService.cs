using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Karate.Services;

/// <summary>
/// Driver backup/restore via Windows' own pnputil — the Driver Genius
/// headline feature, without any third-party database. Both operations
/// need elevation, so each shows one UAC prompt.
/// </summary>
public static class DriverBackupService
{
    /// <summary>Exports every third-party driver package to the target folder.</summary>
    public static async Task<(bool Success, int PackageCount, int ExitCode)> BackupAsync(string targetFolder)
    {
        try
        {
            Directory.CreateDirectory(targetFolder);
            var exitCode = await RunElevatedAsync($"/export-driver * \"{targetFolder}\"");
            var count = Directory.EnumerateFiles(targetFolder, "*.inf", SearchOption.AllDirectories).Count();
            return (exitCode == 0 && count > 0, count, exitCode);
        }
        catch (Win32Exception)
        {
            return (false, 0, -1); // UAC declined
        }
    }

    /// <summary>
    /// Installs every driver package found under the backup folder. Windows
    /// applies only packages matching present hardware. 3010 = reboot needed.
    /// </summary>
    public static async Task<(bool Success, bool RebootNeeded, int ExitCode)> RestoreAsync(string backupFolder)
    {
        try
        {
            var infGlob = Path.Combine(backupFolder, "*.inf");
            var exitCode = await RunElevatedAsync($"/add-driver \"{infGlob}\" /subdirs /install");
            return (exitCode is 0 or 3010, exitCode == 3010, exitCode);
        }
        catch (Win32Exception)
        {
            return (false, false, -1);
        }
    }

    private static async Task<int> RunElevatedAsync(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pnputil.exe",
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        using var process = Process.Start(psi);
        if (process is null)
            return -1;
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
