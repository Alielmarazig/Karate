using System.Reflection;
using System.Windows;
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
        Loaded += (_, _) => SystemThemeWatcher.Watch(this);
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
