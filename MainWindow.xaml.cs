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

        var prompt = new Wpf.Ui.Controls.MessageBox
        {
            Title = "Update available",
            Content = $"Karate {release.TagName} is available — you have v{SelfUpdateService.CurrentVersion.ToString(3)}.\n\n" +
                      "Update now? The app restarts itself when done.",
            PrimaryButtonText = "Update now",
            CloseButtonText = "Later",
        };
        if (await prompt.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
            return;

        vm.IsBusy = true;
        vm.StatusText = $"Downloading Karate {release.TagName}…";
        try
        {
            if (SelfUpdateService.IsInstalledCopy && release.MsiUrl.Length > 0)
            {
                var msi = await SelfUpdateService.DownloadAsync(release.MsiUrl, "KarateUpdate.msi");
                vm.StatusText = "Installing update — Karate will restart…";
                SelfUpdateService.ApplyMsiAndRestart(msi);
            }
            else if (release.ExeUrl.Length > 0)
            {
                var exe = await SelfUpdateService.DownloadAsync(release.ExeUrl, "KaratePortableNew.exe");
                vm.StatusText = "Applying update — Karate will restart…";
                SelfUpdateService.ApplyPortableAndRestart(exe);
            }
            else
            {
                Process.Start(new ProcessStartInfo(release.HtmlUrl) { UseShellExecute = true });
            }
        }
        catch
        {
            vm.StatusText = "Update download failed — try again later or grab it from GitHub Releases.";
        }
        finally
        {
            vm.IsBusy = false;
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
