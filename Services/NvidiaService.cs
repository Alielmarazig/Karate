using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;

namespace Karate.Services;

public record NvidiaDriver(string Version, string DownloadUrl, string DetailsUrl, string ReleaseDate);

/// <summary>
/// Third driver channel: NVIDIA's own driver lookup — the same endpoints
/// nvidia.com uses. Game Ready drivers land here weeks before Microsoft's
/// channels carry them.
/// </summary>
public static class NvidiaService
{
    private const string ProductLookupUrl = "https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3";
    private const string DriverLookupUrl =
        "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php" +
        "?func=DriverManualLookup&psid={0}&pfid={1}&osID={2}&languageCode=1033&beta=0&isWHQL=1&dltype=-1&dch=1&upCRD=0&numberOfResults=1";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 Karate-UpdateMonitor");
        return client;
    }

    /// <summary>Latest Game Ready driver for the named GPU, or null (not NVIDIA / not found / offline).</summary>
    public static async Task<NvidiaDriver?> GetLatestAsync(string gpuName)
    {
        try
        {
            var (psid, pfid) = await LookupProductIdsAsync(gpuName);
            if (psid is null || pfid is null)
                return null;

            // 135 = Windows 11, 57 = Windows 10 x64 in NVIDIA's OS table.
            var osId = Environment.OSVersion.Version.Build >= 22000 ? 135 : 57;
            var json = await Http.GetStringAsync(string.Format(DriverLookupUrl, psid, pfid, osId));

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.GetProperty("Success").GetString() != "1")
                return null;

            var info = root.GetProperty("IDS")[0].GetProperty("downloadInfo");
            var version = info.GetProperty("Version").GetString() ?? "";
            if (version.Length == 0)
                return null;

            return new NvidiaDriver(
                version,
                Uri.UnescapeDataString(info.GetProperty("DownloadURL").GetString() ?? ""),
                Uri.UnescapeDataString(TryGetString(info, "DetailsURL")),
                TryGetString(info, "ReleaseDateTime"));
        }
        catch
        {
            return null;
        }
    }

    private static string TryGetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

    private static async Task<(string? Psid, string? Pfid)> LookupProductIdsAsync(string gpuName)
    {
        var xml = XDocument.Parse(await Http.GetStringAsync(ProductLookupUrl));
        var wanted = gpuName.Trim();

        foreach (var value in xml.Descendants("LookupValue"))
        {
            var name = value.Element("Name")?.Value.Trim() ?? "";
            if (name.Length == 0)
                continue;
            if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "NVIDIA " + wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals("NVIDIA " + name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return (value.Attribute("ParentID")?.Value, value.Element("Value")?.Value);
            }
        }
        return (null, null);
    }

    /// <summary>
    /// WDDM registry version → NVIDIA marketing version:
    /// "32.0.16.1074" → "161074" → last five digits "61074" → "610.74".
    /// </summary>
    public static string MarketingVersionFromWddm(string wddmVersion)
    {
        var parts = wddmVersion.Split('.');
        if (parts.Length < 4)
            return "";
        var digits = parts[2] + parts[3];
        if (digits.Length < 5)
            return "";
        var lastFive = digits[^5..];
        return lastFive.Insert(3, ".");
    }

    /// <summary>True when <paramref name="candidate"/> is a strictly newer marketing version.</summary>
    public static bool IsNewer(string candidate, string installedMarketing) =>
        double.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
        && double.TryParse(installedMarketing, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
        && a > b;
}
