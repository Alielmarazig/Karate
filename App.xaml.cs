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
