using System.IO;
using System.Windows;
using Karate.Services;

namespace Karate;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Diagnostic mode: `Karate.exe --catalog-test <hardware-id>` queries the
        // Microsoft Update Catalog channel and writes the result to a text file.
        var catalogIdx = Array.IndexOf(e.Args, "--catalog-test");
        if (catalogIdx >= 0 && catalogIdx + 1 < e.Args.Length)
        {
            var hwid = e.Args[catalogIdx + 1];
            var latest = SoftwareInventoryDiag.RunCatalogTest(hwid);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "karate-catalog.txt"), latest);
            Shutdown();
            return;
        }

        // Diagnostic mode: `Karate.exe --index-test` runs winget-free detection
        // against the package index and writes the matches to a text file.
        if (e.Args.Contains("--index-test"))
        {
            var report = Task.Run(async () =>
            {
                if (!await WingetIndexService.EnsureIndexAsync())
                    return "index unavailable";
                var apps = SoftwareInventory.ScanAll();
                var upgrades = WingetIndexService.FindUpgrades(apps);
                if (upgrades is null)
                    return "index query failed";
                return $"{apps.Count} apps scanned, {upgrades.Count} upgrades found:\n"
                    + string.Join("\n", upgrades.Select(u => $"{u.App.Name}\t{u.App.Version} -> {u.LatestVersion}\t{u.PackageId}"));
            }).GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "karate-index.txt"), report);
            Shutdown();
            return;
        }

        // Diagnostic mode: `Karate.exe --dump` writes the scan result to a text
        // file and exits, so detection issues can be debugged without the UI.
        if (e.Args.Contains("--dump"))
        {
            var apps = SoftwareInventory.ScanAll();
            var lines = apps.Select(a => $"{a.Name}\t{a.Version}\t{a.Publisher}\t{a.RegistrySource}");
            var path = Path.Combine(AppContext.BaseDirectory, "karate-scan.txt");
            File.WriteAllLines(path, lines.Prepend($"{apps.Count} applications found"));
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }
}
