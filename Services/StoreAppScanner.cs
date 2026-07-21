using Karate.Models;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace Karate.Services;

/// <summary>
/// Enumerates MSIX / Microsoft Store packages for the current user.
/// These apps (Slack, WhatsApp, Terminal, …) never appear in the
/// registry uninstall keys, so the registry scanner cannot see them.
/// </summary>
public static class StoreAppScanner
{
    public static List<InstalledApp> Scan()
    {
        var results = new List<InstalledApp>();
        try
        {
            var packageManager = new PackageManager();
            foreach (var package in packageManager.FindPackagesForUser(string.Empty))
            {
                try
                {
                    if (package.IsFramework || package.IsResourcePackage)
                        continue;
                    // Skip OS-inbox packages (shell components etc.) to reduce noise.
                    if (package.SignatureKind == PackageSignatureKind.System)
                        continue;

                    var name = package.DisplayName;
                    if (string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var publisher = "";
                    try
                    {
                        publisher = package.PublisherDisplayName ?? "";
                        if (publisher.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                            publisher = "";
                    }
                    catch
                    {
                        // Some packages fail to resolve their publisher resource.
                    }

                    var location = "";
                    try
                    {
                        location = package.InstalledLocation?.Path ?? "";
                    }
                    catch
                    {
                        // Staged or partially-removed packages throw here.
                    }

                    var v = package.Id.Version;
                    results.Add(new InstalledApp
                    {
                        Name = name.Trim(),
                        Version = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}",
                        Publisher = publisher.Trim(),
                        InstallLocation = location,
                        RegistrySource = "Microsoft Store",
                    });
                }
                catch
                {
                    // Never let one broken package abort the whole scan.
                }
            }
        }
        catch
        {
            // PackageManager unavailable (e.g. stripped-down Windows) — return what we have.
        }
        return results;
    }
}
