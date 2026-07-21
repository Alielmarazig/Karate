using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Karate.Models;
using Karate.Services;

namespace Karate.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<InstalledApp> Apps { get; } = [];
    public ICollectionView AppsView { get; }
    public DriversViewModel Drivers { get; } = new();

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _updatesOnly;

    [ObservableProperty]
    private string _statusText = "Ready — click Scan to enumerate installed software.";

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _updatesCount;

    [ObservableProperty]
    private int _upToDateCount;

    public MainViewModel()
    {
        AppsView = CollectionViewSource.GetDefaultView(Apps);
        // Updates float to the top; alphabetical within each group.
        AppsView.SortDescriptions.Add(new SortDescription(nameof(InstalledApp.SortRank), ListSortDirection.Ascending));
        AppsView.SortDescriptions.Add(new SortDescription(nameof(InstalledApp.Name), ListSortDirection.Ascending));
        AppsView.Filter = FilterApp;
    }

    private void RefreshCounts()
    {
        TotalCount = Apps.Count;
        UpdatesCount = Apps.Count(a => a.Status is AppStatus.UpdateAvailable or AppStatus.Updating or AppStatus.UpdateFailed);
        UpToDateCount = Apps.Count(a => a.Status is AppStatus.NoKnownUpdate or AppStatus.Updated);
    }

    partial void OnSearchTextChanged(string value) => AppsView.Refresh();

    partial void OnUpdatesOnlyChanged(bool value) => AppsView.Refresh();

    private bool FilterApp(object obj)
    {
        if (obj is not InstalledApp app)
            return false;

        if (UpdatesOnly && app.Status != AppStatus.UpdateAvailable)
            return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        return app.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || app.Publisher.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsBusy = true;
        StatusText = "Scanning installed software (registry + Microsoft Store)…";
        try
        {
            var apps = await Task.Run(SoftwareInventory.ScanAll);
            Apps.Clear();
            foreach (var app in apps)
                Apps.Add(app);
            RefreshCounts();
            StatusText = $"{Apps.Count} applications found.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        if (Apps.Count == 0)
            await ScanAsync();

        IsBusy = true;
        StatusText = "Querying winget for available updates (this can take a minute)…";
        try
        {
            var upgrades = await WingetService.GetUpgradesAsync();
            if (upgrades is null)
            {
                StatusText = "winget was not found on this system — install 'App Installer' from the Microsoft Store.";
                return;
            }

            int updateCount = 0;
            foreach (var app in Apps)
            {
                var match = upgrades.FirstOrDefault(u => NamesMatch(app.Name, u.Name));
                if (match is not null)
                {
                    app.AvailableVersion = match.AvailableVersion;
                    app.WingetId = match.Id;
                    app.Status = AppStatus.UpdateAvailable;
                    updateCount++;
                }
                else
                {
                    app.Status = AppStatus.NoKnownUpdate;
                }
            }

            AppsView.Refresh();
            RefreshCounts();
            StatusText = $"{Apps.Count} applications — {updateCount} updates available.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // winget can't run two installs at once (msiexec mutex) — serialize them.
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    [RelayCommand]
    private async Task UpdateAppAsync(InstalledApp app)
    {
        if (!app.CanUpdate)
            return;

        await _updateLock.WaitAsync();
        try
        {
            app.Status = AppStatus.Updating;
            StatusText = $"Updating {app.Name} — a UAC prompt may appear…";

            var (success, exitCode) = await WingetService.UpgradeAsync(app.WingetId);
            if (success)
            {
                app.Status = AppStatus.Updated;
                StatusText = $"{app.Name} updated to {app.AvailableVersion}.";
            }
            else
            {
                app.Status = AppStatus.UpdateFailed;
                StatusText = $"Update of {app.Name} failed (winget exit code 0x{exitCode:X8}).";
            }
        }
        finally
        {
            _updateLock.Release();
            AppsView.Refresh();
            RefreshCounts();
        }
    }

    [RelayCommand]
    private async Task UpdateAllAsync()
    {
        var pending = Apps.Where(a => a.CanUpdate).ToList();
        if (pending.Count == 0)
        {
            StatusText = "Nothing to update — run Check for Updates first.";
            return;
        }

        int done = 0, failed = 0;
        foreach (var app in pending)
        {
            StatusText = $"Updating {done + failed + 1} of {pending.Count}: {app.Name}…";
            await UpdateAppAsync(app);
            if (app.Status == AppStatus.Updated)
                done++;
            else
                failed++;
        }

        StatusText = failed == 0
            ? $"All {done} updates installed."
            : $"{done} updates installed, {failed} failed — see status per app.";
    }

    private static bool NamesMatch(string registryName, string wingetName)
    {
        var a = registryName.Trim();
        var b = wingetName.Trim();
        if (a.Length == 0 || b.Length == 0)
            return false;

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;

        // winget truncates long names with an ellipsis; match on prefix both ways.
        if (b.Length >= 5 && a.StartsWith(b, StringComparison.OrdinalIgnoreCase))
            return true;
        if (a.Length >= 5 && b.StartsWith(a, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
