using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Karate.Models;

public partial class DriverInfo : UpdatableItem
{
    public required string Name { get; init; }
    public string DeviceClass { get; init; } = "";
    public string Version { get; init; } = "";
    public string DriverDate { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string HardwareId { get; init; } = "";
    public string Source { get; init; } = "Device Manager";

    /// <summary>Catalog search only makes sense with a hardware id to search for.</summary>
    public bool HasHardwareId => HardwareId.Length > 0;

    /// <summary>Windows Update UpdateID when this update came from WU — enables direct install.</summary>
    public string WuUpdateId { get; set; } = "";

    /// <summary>Vendor page for the update when it came from a vendor channel (NVIDIA).</summary>
    public string VendorDownloadUrl { get; set; } = "";

    public bool CanUpdate => Status == AppStatus.UpdateAvailable;

    protected override void OnStatusChangedExtra() => OnPropertyChanged(nameof(CanUpdate));

    /// <summary>How critical the pending update is: Critical / Important / Moderate / Optional.</summary>
    [ObservableProperty]
    private string _severity = "";

    partial void OnSeverityChanged(string value)
    {
        OnPropertyChanged(nameof(SeverityBrush));
        OnPropertyChanged(nameof(SeverityTintBrush));
        OnPropertyChanged(nameof(HasSeverity));
    }

    public bool HasSeverity => Severity.Length > 0;

    private static readonly Brush CriticalBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)));
    private static readonly Brush ImportantBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF7, 0xA8, 0x26)));
    private static readonly Brush ModerateBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0xC5, 0x18)));
    private static readonly Brush LowBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)));

    private static readonly Brush CriticalTint = Freeze(new SolidColorBrush(Color.FromArgb(0x28, 0xE5, 0x48, 0x4D)));
    private static readonly Brush ImportantTint = Freeze(new SolidColorBrush(Color.FromArgb(0x28, 0xF7, 0xA8, 0x26)));
    private static readonly Brush ModerateTint = Freeze(new SolidColorBrush(Color.FromArgb(0x28, 0xF5, 0xC5, 0x18)));
    private static readonly Brush LowTint = Freeze(new SolidColorBrush(Color.FromArgb(0x28, 0x8B, 0x94, 0x9E)));

    public Brush SeverityBrush => Severity switch
    {
        "Critical" => CriticalBrush,
        "Important" => ImportantBrush,
        "Moderate" => ModerateBrush,
        _ => LowBrush,
    };

    /// <summary>Translucent background for the severity badge.</summary>
    public Brush SeverityTintBrush => Severity switch
    {
        "Critical" => CriticalTint,
        "Important" => ImportantTint,
        "Moderate" => ModerateTint,
        _ => LowTint,
    };

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
