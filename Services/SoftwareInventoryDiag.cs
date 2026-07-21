namespace Karate.Services;

/// <summary>Blocking wrappers for the --catalog-test diagnostic flag.</summary>
public static class SoftwareInventoryDiag
{
    public static string RunCatalogTest(string hardwareId)
    {
        // Task.Run keeps the await continuations off the blocked STA startup thread.
        var latest = Task.Run(() => CatalogService.GetLatestAsync(hardwareId)).GetAwaiter().GetResult();
        return latest is null
            ? $"query: {CatalogService.TrimHardwareId(hardwareId)}\nno driver found"
            : $"query: {CatalogService.TrimHardwareId(hardwareId)}\nbest: {latest.Version} ({latest.Date})\ntitle: {latest.Title}";
    }
}
