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
                    match.AvailableVersion = update.Title;
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
                        AvailableVersion = update.Title,
                        Status = AppStatus.UpdateAvailable,
                    });
                }
            }

            DriversView.Refresh();
            RefreshCounts();
            StatusText = UpdatesCount == 0
                ? $"{Drivers.Count} drivers — all up to date according to Windows Update."
                : $"{Drivers.Count} drivers — {UpdatesCount} driver updates available. Install them via Windows Update.";
        }
        finally
        {
            IsBusy = false;
        }
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
        UpdatesCount = Drivers.Count(d => d.Status == AppStatus.UpdateAvailable);
    }

    [RelayCommand]
    private void OpenWindowsUpdate() =>
        Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true });
}
