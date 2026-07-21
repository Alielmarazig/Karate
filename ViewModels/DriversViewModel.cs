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

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _progressIndeterminate = true;

    [ObservableProperty]
    private string _progressDetail = "";

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
        ProgressIndeterminate = true;
        ProgressDetail = "";
        StatusText = "Scanning installed drivers…";
        try
        {
            var drivers = await Task.Run(DriverScanner.Scan);
            Drivers.Clear();
            foreach (var driver in drivers)
                Drivers.Add(driver);
            RefreshCounts();
            ProgressDetail = $"{Drivers.Count} drivers scanned";
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
        ProgressIndeterminate = true;
        ProgressDetail = "phase 1/2 — Windows Update";
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
            {
                driver.Status = AppStatus.NoKnownUpdate;
                driver.Severity = "";
            }

            foreach (var update in updates)
            {
                var match = FindMatch(update);
                if (match is not null)
                {
                    match.AvailableVersion = CompactLabel(update.Title);
                    match.WuUpdateId = update.UpdateId;
                    match.Severity = ComputeSeverity(update.MsrcSeverity, update.IsMandatory, update.AutoSelect, update.BrowseOnly, match.DeviceClass);
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
                        AvailableVersion = CompactLabel(update.Title),
                        WuUpdateId = update.UpdateId,
                        Severity = ComputeSeverity(update.MsrcSeverity, update.IsMandatory, update.AutoSelect, update.BrowseOnly, ""),
                        Status = AppStatus.UpdateAvailable,
                    });
                }
            }

            // Second channel: Microsoft Update Catalog, for the device classes
            // where driver updates actually matter.
            await CheckCatalogAsync();

            // Third channel: NVIDIA's own lookup — Game Ready drivers arrive
            // here weeks before Microsoft's channels.
            await CheckNvidiaAsync();

            DriversView.Refresh();
            RefreshCounts();
            StatusText = UpdatesCount == 0
                ? $"{Drivers.Count} drivers — no newer drivers found (Windows Update + Update Catalog + NVIDIA)."
                : $"{Drivers.Count} drivers — {UpdatesCount} updates found. Click Update on a row to install.";
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

        ProgressIndeterminate = false;
        ProgressValue = 0;

        int i = 0;
        foreach (var group in groups)
        {
            i++;
            ProgressDetail = $"phase 2/2 — {i - 1}/{groups.Count} devices checked · {groups.Count - i + 1} remaining";
            ProgressValue = (i - 1) * 100.0 / groups.Count;
            StatusText = $"Checking Microsoft Update Catalog ({i}/{groups.Count}): {group.First().Name}…";

            var latest = await CatalogService.GetLatestAsync(group.Key);
            if (latest is null)
            {
                ProgressValue = i * 100.0 / groups.Count;
                continue;
            }

            foreach (var driver in group)
            {
                if (Version.TryParse(driver.Version, out var installed) && latest.Version > installed)
                {
                    driver.AvailableVersion = latest.Version.ToString();
                    driver.Severity = ComputeSeverity("", false, false, false, driver.DeviceClass);
                    driver.Status = AppStatus.UpdateAvailable;
                }
            }

            ProgressValue = i * 100.0 / groups.Count;
            await Task.Delay(250); // be polite to the catalog
        }

        ProgressValue = 100;
        ProgressDetail = groups.Count > 0
            ? $"{groups.Count}/{groups.Count} devices checked"
            : "";
    }

    private async Task CheckNvidiaAsync()
    {
        var gpu = Drivers.FirstOrDefault(d =>
            d.DeviceClass.Equals("DISPLAY", StringComparison.OrdinalIgnoreCase)
            && d.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        if (gpu is null)
            return;

        ProgressIndeterminate = true;
        ProgressDetail = "phase 3/3 — NVIDIA";
        StatusText = $"Checking NVIDIA for a newer Game Ready driver ({gpu.Name})…";

        var latest = await NvidiaService.GetLatestAsync(gpu.Name);
        if (latest is null)
            return;

        var installed = NvidiaService.MarketingVersionFromWddm(gpu.Version);
        if (installed.Length == 0 || !NvidiaService.IsNewer(latest.Version, installed))
            return;

        // Vendor-direct wins: it is virtually always the newest package.
        gpu.AvailableVersion = latest.Version;
        gpu.WuUpdateId = "";
        gpu.VendorDownloadUrl = latest.DetailsUrl.Length > 0 ? latest.DetailsUrl : latest.DownloadUrl;
        gpu.Status = AppStatus.UpdateAvailable;
    }

    [RelayCommand]
    private void OpenCatalog(DriverInfo driver) =>
        Process.Start(new ProcessStartInfo(CatalogService.SearchUrl(driver.HardwareId)) { UseShellExecute = true });

    [RelayCommand]
    private async Task UpdateDriverAsync(DriverInfo driver)
    {
        if (driver.Status != AppStatus.UpdateAvailable)
            return;

        // Vendor-direct (NVIDIA): open the official download page.
        if (string.IsNullOrEmpty(driver.WuUpdateId) && driver.VendorDownloadUrl.Length > 0)
        {
            Process.Start(new ProcessStartInfo(driver.VendorDownloadUrl) { UseShellExecute = true });
            StatusText = $"Opened NVIDIA's download page for {driver.Name} — download and run the installer.";
            return;
        }

        // Catalog-flagged drivers can't be auto-installed safely — hand the user
        // the signed package straight from Microsoft instead.
        if (string.IsNullOrEmpty(driver.WuUpdateId))
        {
            OpenCatalog(driver);
            StatusText = $"Opened the Update Catalog for {driver.Name} — use its Download button, then run the installer.";
            return;
        }

        driver.Status = AppStatus.Updating;
        IsBusy = true;
        ProgressIndeterminate = true;
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
            IsBusy = false;
            DriversView.Refresh();
            RefreshCounts();
        }
    }

    /// <summary>
    /// Severity on Microsoft's MSRC rating scale (Critical / Important / Moderate / Low),
    /// in order of authority:
    /// 1. The update's own MSRC severity rating, when Microsoft published one.
    /// 2. Windows Update deployment metadata per Microsoft's driver-distribution rules:
    ///    mandatory → Critical; automatic/"Recommended" → Important; browse-only ("Optional") → Low.
    /// 3. Device-class fallback (Catalog findings carry no Microsoft metadata).
    /// </summary>
    private static string ComputeSeverity(string msrcSeverity, bool isMandatory, bool autoSelect, bool browseOnly, string deviceClass)
    {
        if (!string.IsNullOrWhiteSpace(msrcSeverity))
            return Normalize(msrcSeverity);
        if (isMandatory)
            return "Critical";
        if (autoSelect)
            return "Important";
        if (browseOnly)
            return "Low";
        return deviceClass.ToUpperInvariant() switch
        {
            "DISPLAY" or "NET" or "HDC" or "SCSIADAPTER" or "SYSTEM" => "Important",
            "MEDIA" or "USB" or "BLUETOOTH" => "Moderate",
            _ => "Low",
        };

        static string Normalize(string s) => s.Trim().ToLowerInvariant() switch
        {
            "critical" => "Critical",
            "important" => "Important",
            "moderate" => "Moderate",
            _ => "Low",
        };
    }

    /// <summary>"Intel Corporation - Net - 23.100.0.5" → "23.100.0.5".</summary>
    private static string CompactLabel(string title)
    {
        var m = System.Text.RegularExpressions.Regex.Match(title, @"\d+\.\d+(\.\d+){1,3}");
        return m.Success ? m.Value : title;
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

    [RelayCommand]
    private async Task BackupDriversAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose where to save the driver backup" };
        if (dialog.ShowDialog() != true)
            return;

        var target = System.IO.Path.Combine(dialog.FolderName, $"KarateDriverBackup-{DateTime.Now:yyyyMMdd-HHmm}");
        IsBusy = true;
        ProgressIndeterminate = true;
        StatusText = "Backing up all third-party drivers — approve the UAC prompt. This can take a few minutes…";
        try
        {
            var (ok, count, exitCode) = await DriverBackupService.BackupAsync(target);
            StatusText = ok
                ? $"Backed up {count} driver packages to {target}."
                : $"Driver backup failed or was cancelled (exit code {exitCode}).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreDriversAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a Karate driver backup folder" };
        if (dialog.ShowDialog() != true)
            return;

        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = "Restore drivers",
            Content = $"Install all driver packages from:\n{dialog.FolderName}\n\n" +
                      "Windows only applies packages that match your hardware. A reboot may be required.",
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
        };
        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
            return;

        IsBusy = true;
        ProgressIndeterminate = true;
        StatusText = "Restoring drivers — approve the UAC prompt. This can take a few minutes…";
        try
        {
            var (ok, rebootNeeded, exitCode) = await DriverBackupService.RestoreAsync(dialog.FolderName);
            StatusText = ok
                ? rebootNeeded
                    ? "Drivers restored — restart Windows to finish."
                    : "Drivers restored."
                : $"Driver restore failed or was cancelled (exit code {exitCode}).";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
