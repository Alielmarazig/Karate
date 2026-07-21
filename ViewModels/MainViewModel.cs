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

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _updatesOnly;

    [ObservableProperty]
    private string _statusText = "Ready — click Scan to enumerate installed software.";

    public MainViewModel()
    {
        AppsView = CollectionViewSource.GetDefaultView(Apps);
        AppsView.SortDescriptions.Add(new SortDescription(nameof(InstalledApp.Name), ListSortDirection.Ascending));
        AppsView.Filter = FilterApp;
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
                    app.Status = AppStatus.UpdateAvailable;
                    updateCount++;
                }
                else
                {
                    app.Status = AppStatus.NoKnownUpdate;
                }
            }

            AppsView.Refresh();
            StatusText = $"{Apps.Count} applications — {updateCount} updates available.";
        }
        finally
        {
            IsBusy = false;
        }
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
