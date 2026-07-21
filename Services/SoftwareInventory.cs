using Karate.Models;

namespace Karate.Services;

public static class SoftwareInventory
{
    /// <summary>Merges registry-installed and Store/MSIX apps, deduplicated by name and version.</summary>
    public static List<InstalledApp> ScanAll()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = new List<InstalledApp>();
        foreach (var app in RegistryScanner.Scan().Concat(StoreAppScanner.Scan()))
        {
            if (seen.Add($"{app.Name}|{app.Version}"))
                all.Add(app);
        }
        all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return all;
    }
}
