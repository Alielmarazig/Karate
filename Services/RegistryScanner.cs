using Karate.Models;
using Microsoft.Win32;

namespace Karate.Services;

public static class RegistryScanner
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly (RegistryHive Hive, RegistryView View, string Label)[] Roots =
    [
        (RegistryHive.LocalMachine, RegistryView.Registry64, "Machine (64-bit)"),
        (RegistryHive.LocalMachine, RegistryView.Registry32, "Machine (32-bit)"),
        (RegistryHive.CurrentUser, RegistryView.Default, "User"),
    ];

    public static List<InstalledApp> Scan()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<InstalledApp>();

        foreach (var (hive, view, label) in Roots)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstallKey = baseKey.OpenSubKey(UninstallPath);
            if (uninstallKey is null)
                continue;

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                using var appKey = uninstallKey.OpenSubKey(subKeyName);
                if (appKey is null)
                    continue;

                var app = ReadApp(appKey, label);
                if (app is not null && seen.Add($"{app.Name}|{app.Version}"))
                    results.Add(app);
            }
        }

        results.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return results;
    }

    private static InstalledApp? ReadApp(RegistryKey key, string sourceLabel)
    {
        var name = (key.GetValue("DisplayName") as string)?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        // Hidden system components and child entries of larger installers.
        if (GetInt(key, "SystemComponent") == 1)
            return null;
        if (key.GetValue("ParentKeyName") is string)
            return null;

        // Windows updates / hotfix entries.
        var releaseType = key.GetValue("ReleaseType") as string;
        if (releaseType is "Security Update" or "Update Rollup" or "Hotfix")
            return null;

        return new InstalledApp
        {
            Name = name,
            Version = (key.GetValue("DisplayVersion") as string)?.Trim() ?? "",
            Publisher = (key.GetValue("Publisher") as string)?.Trim() ?? "",
            InstallLocation = (key.GetValue("InstallLocation") as string)?.Trim() ?? "",
            RegistrySource = sourceLabel,
        };
    }

    private static int GetInt(RegistryKey key, string valueName)
    {
        try
        {
            return key.GetValue(valueName) is int i ? i : 0;
        }
        catch
        {
            return 0;
        }
    }
}
