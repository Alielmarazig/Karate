using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Karate.Models;

public enum AppStatus
{
    NotChecked,
    UpdateAvailable,
    NoKnownUpdate,
    Updating,
    Updated,
    UpdateFailed,
}

/// <summary>Base for anything Karate can check for updates (apps, drivers).</summary>
public abstract partial class UpdatableItem : ObservableObject
{
    [ObservableProperty]
    private string _availableVersion = "";

    [ObservableProperty]
    private AppStatus _status = AppStatus.NotChecked;

    partial void OnStatusChanged(AppStatus value)
    {
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(SortRank));
        OnStatusChangedExtra();
    }

    /// <summary>Lets derived types raise change notifications for their own status-derived properties.</summary>
    protected virtual void OnStatusChangedExtra()
    {
    }

    /// <summary>Items needing attention sort before everything else.</summary>
    public int SortRank => Status is AppStatus.UpdateAvailable or AppStatus.Updating or AppStatus.UpdateFailed ? 0 : 1;

    public string StatusDisplay => Status switch
    {
        AppStatus.UpdateAvailable => "Update available",
        AppStatus.NoKnownUpdate => "Up to date",
        AppStatus.Updating => "Updating…",
        AppStatus.Updated => "Updated",
        AppStatus.UpdateFailed => "Update failed",
        _ => "Not checked",
    };

    private static readonly Brush OrangeBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF7, 0xA8, 0x26)));
    private static readonly Brush GreenBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50)));
    private static readonly Brush GrayBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)));
    private static readonly Brush BlueBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x58, 0xA6, 0xFF)));
    private static readonly Brush RedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF8, 0x51, 0x49)));

    public Brush StatusBrush => Status switch
    {
        AppStatus.UpdateAvailable => OrangeBrush,
        AppStatus.NoKnownUpdate or AppStatus.Updated => GreenBrush,
        AppStatus.Updating => BlueBrush,
        AppStatus.UpdateFailed => RedBrush,
        _ => GrayBrush,
    };

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
