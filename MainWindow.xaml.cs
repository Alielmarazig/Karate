using System.Diagnostics;
using System.Reflection;
using System.Windows;
using Karate.Services;
using Karate.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Karate;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        // Brand accent (violet) drives primary buttons, checkboxes, focus rings…
        ApplicationAccentColorManager.Apply(System.Windows.Media.Color.FromRgb(0x6C, 0x63, 0xFF));
        // Dark-locked: the painted glass background is designed for dark chrome.
        Loaded += async (_, _) => await CheckForSelfUpdateAsync(explicitCheck: false);
    }

    private ReleaseInfo? _pendingRelease;

    private async void OnCheckSelfUpdateClick(object sender, RoutedEventArgs e) =>
        await CheckForSelfUpdateAsync(explicitCheck: true);

    private async Task CheckForSelfUpdateAsync(bool explicitCheck)
    {
        var vm = (MainViewModel)DataContext;
        var release = await SelfUpdateService.CheckAsync();

        if (release is null || release.Version <= SelfUpdateService.CurrentVersion)
        {
            if (explicitCheck)
                vm.StatusText = release is null
                    ? "Could not reach GitHub to check for updates."
                    : $"You're on the latest version ({SelfUpdateService.CurrentVersion.ToString(3)}).";
            return;
        }

        _pendingRelease = release;
        vm.SelfUpdateLabel = $"Update to {release.TagName}";
        vm.SelfUpdateAvailable = true;
        vm.StatusText = $"Karate {release.TagName} is available — click the update button in the banner.";
    }

    private async void OnHeroUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_pendingRelease is null)
            return;

        var vm = (MainViewModel)DataContext;
        vm.SelfUpdateAvailable = false;
        vm.SelfUpdateDownloading = true;
        vm.SelfUpdateProgress = 0;
        vm.StatusText = $"Downloading Karate {_pendingRelease.TagName}…";
        var progress = new Progress<double>(p => vm.SelfUpdateProgress = p);

        try
        {
            if (SelfUpdateService.IsInstalledCopy && _pendingRelease.MsiUrl.Length > 0)
            {
                var msi = await SelfUpdateService.DownloadAsync(_pendingRelease.MsiUrl, "KarateUpdate.msi", progress);
                vm.StatusText = "Installing update — Karate will restart…";
                SelfUpdateService.ApplyMsiAndRestart(msi);
            }
            else if (_pendingRelease.ExeUrl.Length > 0)
            {
                var exe = await SelfUpdateService.DownloadAsync(_pendingRelease.ExeUrl, "KaratePortableNew.exe", progress);
                vm.StatusText = "Applying update — Karate will restart…";
                SelfUpdateService.ApplyPortableAndRestart(exe);
            }
            else
            {
                Process.Start(new ProcessStartInfo(_pendingRelease.HtmlUrl) { UseShellExecute = true });
                vm.SelfUpdateDownloading = false;
                vm.SelfUpdateAvailable = true;
            }
        }
        catch
        {
            vm.StatusText = "Update download failed — try again later or grab it from GitHub Releases.";
            vm.SelfUpdateDownloading = false;
            vm.SelfUpdateAvailable = true;
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private async void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var about = new Wpf.Ui.Controls.MessageBox
        {
            Title = "About Karate",
            Content = $"Karate — Software Update Monitor\nVersion {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?"}\n\nDeveloped by Alucard GGhz\n© 2026",
            CloseButtonText = "OK",
        };
        await about.ShowDialogAsync();
    }
}
