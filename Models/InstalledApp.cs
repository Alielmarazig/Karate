using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Karate.Models;

public enum AppStatus
{
    NotChecked,
    UpdateAvailable,
    NoKnownUpdate,
}

public partial class InstalledApp : ObservableObject
{
    public required string Name { get; init; }
    public string Version { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string InstallLocation { get; init; } = "";
    public string RegistrySource { get; init; } = "";

    [ObservableProperty]
    private string _availableVersion = "";

    [ObservableProperty]
    private AppStatus _status = AppStatus.NotChecked;

    partial void OnStatusChanged(AppStatus value)
    {
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(StatusBrush));
    }

    public string StatusDisplay => Status switch
    {
        AppStatus.UpdateAvailable => "Update available",
        AppStatus.NoKnownUpdate => "Up to date",
        _ => "Not checked",
    };

    private static readonly Brush OrangeBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF7, 0xA8, 0x26)));
    private static readonly Brush GreenBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50)));
    private static readonly Brush GrayBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)));

    public Brush StatusBrush => Status switch
    {
        AppStatus.UpdateAvailable => OrangeBrush,
        AppStatus.NoKnownUpdate => GreenBrush,
        _ => GrayBrush,
    };

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
