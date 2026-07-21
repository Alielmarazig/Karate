using System.Management;
using Karate.Models;

namespace Karate.Services;

/// <summary>Enumerates installed device drivers via WMI (Win32_PnPSignedDriver).</summary>
public static class DriverScanner
{
    public static List<DriverInfo> Scan()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<DriverInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, DriverVersion, DriverDate, Manufacturer, DeviceClass, HardWareID " +
                "FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL");
            foreach (var mo in searcher.Get())
            {
                using (mo)
                {
                    var name = (mo["DeviceName"] as string)?.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    var version = (mo["DriverVersion"] as string)?.Trim() ?? "";
                    if (!seen.Add($"{name}|{version}"))
                        continue;

                    // DMTF datetime (yyyymmddHHMMSS…). Take the date digits verbatim —
                    // converting via ManagementDateTimeConverter shifts to local time and
                    // shows the previous day in timezones west of UTC.
                    var date = "";
                    if (mo["DriverDate"] is string dmtf && dmtf.Length >= 8)
                        date = $"{dmtf[..4]}-{dmtf[4..6]}-{dmtf[6..8]}";

                    results.Add(new DriverInfo
                    {
                        Name = name,
                        Version = version,
                        DeviceClass = (mo["DeviceClass"] as string)?.Trim() ?? "",
                        Manufacturer = (mo["Manufacturer"] as string)?.Trim() ?? "",
                        DriverDate = date,
                        HardwareId = (mo["HardWareID"] as string)?.Trim() ?? "",
                    });
                }
            }
        }
        catch
        {
            // WMI unavailable — return whatever was collected.
        }
        results.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return results;
    }
}
