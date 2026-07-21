using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Karate.Services;

public record CatalogDriver(string Title, Version Version, string Date);

/// <summary>
/// Second driver-update channel: the Microsoft Update Catalog
/// (catalog.update.microsoft.com). It indexes far more driver versions than
/// Windows Update offers a given machine, searchable by hardware id.
/// </summary>
public static class CatalogService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 Karate-UpdateMonitor/0.3");
        return client;
    }

    private static readonly Regex LinkRegex = new(
        @"id='([0-9a-fA-F-]{36})_link'[^>]*>\s*(.*?)\s*</a>", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>PCI\VEN_xxxx&amp;DEV_xxxx&amp;SUBSYS…&amp;REV… → PCI\VEN_xxxx&amp;DEV_xxxx (what the catalog indexes best).</summary>
    public static string TrimHardwareId(string hardwareId)
    {
        var cut = hardwareId.IndexOf("&SUBSYS", StringComparison.OrdinalIgnoreCase);
        if (cut < 0)
            cut = hardwareId.IndexOf("&REV", StringComparison.OrdinalIgnoreCase);
        return cut > 0 ? hardwareId[..cut] : hardwareId;
    }

    public static string SearchUrl(string hardwareId) =>
        "https://www.catalog.update.microsoft.com/Search.aspx?q=" + Uri.EscapeDataString(TrimHardwareId(hardwareId));

    /// <summary>
    /// Returns the newest driver the catalog lists for this hardware id, or null
    /// when nothing was found (or the page could not be fetched/parsed).
    /// </summary>
    public static async Task<CatalogDriver?> GetLatestAsync(string hardwareId)
    {
        string html;
        try
        {
            html = await Http.GetStringAsync(SearchUrl(hardwareId));
        }
        catch
        {
            return null;
        }

        CatalogDriver? best = null;
        foreach (Match link in LinkRegex.Matches(html))
        {
            var guid = link.Groups[1].Value;
            var title = WebUtility.HtmlDecode(link.Groups[2].Value).Trim();

            // Row cells: C3 = classification, C4 = last-updated date, C5 = version.
            var classification = Cell(html, guid, 3);
            if (!classification.Contains("Driver", StringComparison.OrdinalIgnoreCase))
                continue;

            var versionText = Cell(html, guid, 5);
            if (!Version.TryParse(versionText, out var version))
            {
                // Fall back to a version embedded in the title, e.g. "… (32.0.15.9637)".
                var m = Regex.Match(title, @"\d+\.\d+(\.\d+){1,2}");
                if (!m.Success || !Version.TryParse(m.Value, out version!))
                    continue;
            }

            if (best is null || version > best.Version)
                best = new CatalogDriver(title, version, Cell(html, guid, 4));
        }
        return best;
    }

    private static string Cell(string html, string guid, int column)
    {
        var m = Regex.Match(html, $@"id=""{guid}_C{column}_R\d+""[^>]*>\s*(.*?)\s*</td>", RegexOptions.Singleline);
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value).Trim() : "";
    }
}
