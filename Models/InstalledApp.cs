namespace Karate.Models;

public partial class InstalledApp : UpdatableItem
{
    public required string Name { get; init; }
    public string Version { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string InstallLocation { get; init; } = "";
    public string RegistrySource { get; init; } = "";

    /// <summary>winget package id, filled in when an update match is found.</summary>
    public string WingetId { get; set; } = "";

    /// <summary>Row Update button visibility: needs an update and a usable winget id.</summary>
    public bool CanUpdate => Status == AppStatus.UpdateAvailable
        && !string.IsNullOrWhiteSpace(WingetId)
        && !WingetId.Contains('…');

    protected override void OnStatusChangedExtra() => OnPropertyChanged(nameof(CanUpdate));
}
