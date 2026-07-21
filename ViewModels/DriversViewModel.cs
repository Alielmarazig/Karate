using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Karate.Models;
using Karate.Services;

namespace Karate.ViewModels;

public partial class DriversViewModel : ObservableObject
{
    public ObservableCollection<DriverInfo> Drivers { get; } = [];
    public ICollectionView DriversView { get; }

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _updatesOnly;

    [ObservableProperty]
    private string _statusText = "Ready — click Scan to enumerate installed drivers.";

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _updatesCount;

    public DriversViewModel()
    {
        DriversView = CollectionViewSource.GetDefaultView(Drivers);
        DriversView.SortDescriptions.Add(new SortDescription(nameof(DriverInfo.SortRank), ListSortDirection.Ascending));
        DriversView.SortDescriptions.Add(new SortDescription(nameof(DriverInfo.Name), ListSortDirection.Ascending));
        DriversView.Filter = FilterDriver;
    }

    partial void OnSearchTextChanged(string value) => DriversView.Refresh();

    partial void OnUpdatesOnlyChanged(bool value) => DriversView.Refresh();

    private bool FilterDriver(object obj)
    {
        if (obj is not DriverInfo driver)
            return false;

        if (UpdatesOnly && driver.Status != AppStatus.UpdateAvailable)
            return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        return driver.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || driver.Manufacturer.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || driver.DeviceClass.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsBusy = true;
        StatusText = "Scanning installed drivers…";
        try
        {
            var drivers = await Task.Run(DriverScanner.Scan);
            Drivers.Clear();
            foreach (var driver in drivers)
                Drivers.Add(driver);
            RefreshCounts();
            StatusText = $"{Drivers.Count} drivers found.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        if (Drivers.Count == 0)
            await ScanAsync();

        IsBusy = true;
        StatusText = "Searching Windows Update for driver updates (this can take a few minutes)…";
        try
        {
            var updates = await DriverUpdateService.SearchAsync();
            if (updates is null)
            {
                StatusText = "Windows Update search failed — the service may be disabled or offline.";
                return;
            }

            // Drop rows added by a previous check so repeat checks don't duplicate them.
            foreach (var stale in Drivers.Where(d => d.Source == "Windows Update").ToList())
                Drivers.Remove(stale);

            foreach (var driver in Drivers)
                driver.Status = AppStatus.NoKnownUpdate;

            foreach (var update in updates)
            {
                var match = FindMatch(update);
                if (match is not null)
                {
                    match.AvailableVersion = CompactLabel(update.Title, "Windows Update");
                    match.WuUpdateId = update.UpdateId;
                    match.Status = AppStatus.UpdateAvailable;
                }
                else
                {
                    // Windows Update knows a driver we couldn't pair with a scanned
                    // device — still worth showing.
                    Drivers.Add(new DriverInfo
                    {
                        Name = string.IsNullOrWhiteSpace(update.Model) ? update.Title : update.Model,
                        Manufacturer = update.Manufacturer,
                        HardwareId = update.HardwareId,
                        Source = "Windows Update",
                        AvailableVersion = CompactLabel(update.Title, "Windows Update"),
                        WuUpdateId = update.UpdateId,
                        Status = AppStatus.UpdateAvailable,
                    });
                }
            }

            // Second channel: Microsoft Update Catalog, for the device classes
            // where driver updates actually matter.
            await CheckCatalogAsync();

            DriversView.Refresh();
            RefreshCounts();
            StatusText = UpdatesCount == 0
                ? $"{Drivers.Count} drivers — no newer drivers found (Windows Update + Update Catalog)."
                : $"{Drivers.Count} drivers — {UpdatesCount} updates found. Windows Update installs them; the Catalog button downloads directly.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static readonly string[] CatalogClasses =
        ["DISPLAY", "NET", "MEDIA", "BLUETOOTH", "HDC", "SCSIADAPTER", "USB"];

    private async Task CheckCatalogAsync()
    {
        var groups = Drivers
            .Where(d => d.Source == "Device Manager"
                && d.Status != AppStatus.UpdateAvailable
                && CatalogClasses.Contains(d.DeviceClass, StringComparer.OrdinalIgnoreCase)
                && (d.HardwareId.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase)
                    || d.HardwareId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)))
            .GroupBy(d => CatalogService.TrimHardwareId(d.HardwareId), StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        int i = 0;
        foreach (var group in groups)
        {
            i++;
            StatusText = $"Checking Microsoft Update Catalog ({i}/{groups.Count}): {group.First().Name}…";

            var latest = await CatalogService.GetLatestAsync(group.Key);
            if (latest is null)
                continue;

            foreach (var driver in group)
            {
                if (Version.TryParse(driver.Version, out var installed) && latest.Version > installed)
                {
                    driver.AvailableVersion = $"{latest.Version} (Catalog)";
                    driver.Status = AppStatus.UpdateAvailable;
                }
            }

            await Task.Delay(250); // be polite to the catalog
        }
    }

    [RelayCommand]
    private void OpenCatalog(DriverInfo driver) =>
        Process.Start(new ProcessStartInfo(CatalogService.SearchUrl(driver.HardwareId)) { UseShellExecute = true });

    [RelayCommand]
    private async Task UpdateDriverAsync(DriverInfo driver)
    {
        if (driver.Status != AppStatus.UpdateAvailable)
            return;

        // Catalog-flagged drivers can't be auto-installed safely — hand the user
        // the signed package straight from Microsoft instead.
        if (string.IsNullOrEmpty(driver.WuUpdateId))
        {
            OpenCatalog(driver);
            StatusText = $"Opened the Update Catalog for {driver.Name} — use its Download button, then run the installer.";
            return;
        }

        driver.Status = AppStatus.Updating;
        StatusText = $"Installing driver update for {driver.Name} — approve the UAC prompt. This can take a while…";
        try
        {
            var ok = await DriverUpdateService.InstallAsync(driver.WuUpdateId);
            driver.Status = ok ? AppStatus.Updated : AppStatus.UpdateFailed;
            StatusText = ok
                ? $"Driver for {driver.Name} installed — a reboot may be required."
                : $"Driver update for {driver.Name} failed or was cancelled.";
        }
        finally
        {
            DriversView.Refresh();
            RefreshCounts();
        }
    }

    /// <summary>"Intel Corporation - Net - 23.100.0.5" → "23.100.0.5 (Windows Update)".</summary>
    private static string CompactLabel(string title, string channel)
    {
        var m = System.Text.RegularExpressions.Regex.Match(title, @"\d+\.\d+(\.\d+){1,3}");
        return m.Success ? $"{m.Value} ({channel})" : title;
    }

    private DriverInfo? FindMatch(DriverUpdate update)
    {
        if (!string.IsNullOrWhiteSpace(update.HardwareId))
        {
            var byHwid = Drivers.FirstOrDefault(d =>
                !string.IsNullOrWhiteSpace(d.HardwareId)
                && (d.HardwareId.Contains(update.HardwareId, StringComparison.OrdinalIgnoreCase)
                    || update.HardwareId.Contains(d.HardwareId, StringComparison.OrdinalIgnoreCase)));
            if (byHwid is not null)
                return byHwid;
        }

        if (!string.IsNullOrWhiteSpace(update.Model))
            return Drivers.FirstOrDefault(d => string.Equals(d.Name, update.Model, StringComparison.OrdinalIgnoreCase));

        return null;
    }

    private void RefreshCounts()
    {
        TotalCount = Drivers.Count;
        UpdatesCount = Drivers.Count(d => d.Status is AppStatus.UpdateAvailable or AppStatus.Updating or AppStatus.UpdateFailed);
    }

    [RelayCommand]
    private void OpenWindowsUpdate() =>
        Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true });
}
