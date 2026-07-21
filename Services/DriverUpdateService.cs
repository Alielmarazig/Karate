using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Karate.Services;

public record DriverUpdate(string Title, string Model, string Manufacturer, string HardwareId, string UpdateId,
    string MsrcSeverity, bool IsMandatory, bool AutoSelect, bool BrowseOnly);

/// <summary>
/// Searches Windows Update for available driver updates via the Windows Update
/// Agent COM API (Microsoft.Update.Session). Search only — installation stays
/// in the user's hands via Windows Update itself.
/// </summary>
public static class DriverUpdateService
{
    public static Task<List<DriverUpdate>?> SearchAsync() => Task.Run(Search);

    private static List<DriverUpdate>? Search()
    {
        try
        {
            var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (sessionType is null)
                return null;
            var session = Activator.CreateInstance(sessionType);
            if (session is null)
                return null;

            var searcher = Invoke(session, "CreateUpdateSearcher");
            if (searcher is null)
                return null;
            SetProp(searcher, "Online", true);

            var result = Invoke(searcher, "Search", "IsInstalled=0 and Type='Driver'");
            var updates = GetProp(result, "Updates");
            if (updates is null)
                return null;

            var count = (int)(GetProp(updates, "Count") ?? 0);
            var list = new List<DriverUpdate>();
            for (int i = 0; i < count; i++)
            {
                var update = GetProp(updates, "Item", i);
                if (update is null)
                    continue;
                var updateId = "";
                try
                {
                    var identity = GetProp(update, "Identity");
                    if (identity is not null)
                        updateId = GetStr(identity, "UpdateID");
                }
                catch
                {
                    // No identity — installable targeting just won't be available.
                }
                bool mandatory = false, autoSelect = false, browseOnly = false;
                try { mandatory = GetProp(update, "IsMandatory") is bool m && m; }
                catch { }
                try { autoSelect = GetProp(update, "AutoSelectOnWebSites") is bool a && a; }
                catch { }
                try { browseOnly = GetProp(update, "BrowseOnly") is bool b && b; }
                catch { }

                list.Add(new DriverUpdate(
                    GetStr(update, "Title"),
                    GetStr(update, "DriverModel"),
                    GetStr(update, "DriverManufacturer"),
                    GetStr(update, "DriverHardwareID"),
                    updateId,
                    GetStr(update, "MsrcSeverity"),
                    mandatory, autoSelect, browseOnly));
            }
            return list;
        }
        catch
        {
            // COM failure, no network, WU service disabled, …
            return null;
        }
    }

    // Downloads and installs one specific Windows Update driver update through
    // an elevated PowerShell helper (WUA install requires admin — this is the
    // supported route; the user approves a single UAC prompt).
    private const string InstallHelperScript = """
        param([Parameter(Mandatory=$true)][string]$UpdateId)
        $ErrorActionPreference = 'Stop'
        try {
            $session = New-Object -ComObject Microsoft.Update.Session
            $searcher = $session.CreateUpdateSearcher()
            $result = $searcher.Search("UpdateID='$UpdateId'")
            if ($result.Updates.Count -eq 0) { exit 3 }
            $coll = New-Object -ComObject Microsoft.Update.UpdateColl
            foreach ($u in $result.Updates) {
                if (-not $u.EulaAccepted) { $u.AcceptEula() }
                [void]$coll.Add($u)
            }
            $downloader = $session.CreateUpdateDownloader()
            $downloader.Updates = $coll
            [void]$downloader.Download()
            $installer = $session.CreateUpdateInstaller()
            $installer.Updates = $coll
            $ir = $installer.Install()
            if ($ir.ResultCode -eq 2 -or $ir.ResultCode -eq 3) { exit 0 } else { exit $ir.ResultCode }
        } catch { exit 9 }
        """;

    /// <summary>Installs one WU driver update by UpdateID. Returns false on failure or UAC decline.</summary>
    public static async Task<bool> InstallAsync(string updateId)
    {
        // The id is interpolated into a PowerShell string — only accept real GUIDs.
        if (!Guid.TryParse(updateId, out var guid))
            return false;

        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "Karate");
            Directory.CreateDirectory(dir);
            var scriptPath = Path.Combine(dir, "install-driver-update.ps1");
            await File.WriteAllTextAsync(scriptPath, InstallHelperScript);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\" -UpdateId \"{guid}\"",
                UseShellExecute = true,
                Verb = "runas",
            };

            using var process = Process.Start(psi);
            if (process is null)
                return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            // UAC prompt declined.
            return false;
        }
    }

    // Late-bound IDispatch helpers — the WUA interop assembly is not available
    // for .NET Core, so we go through reflection.
    private static object? Invoke(object target, string name, params object[] args) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);

    private static object? GetProp(object? target, string name, params object[] args) =>
        target?.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, args);

    private static void SetProp(object target, string name, object value) =>
        target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, [value]);

    private static string GetStr(object target, string name)
    {
        try { return GetProp(target, name) as string ?? ""; }
        catch { return ""; }
    }
}
