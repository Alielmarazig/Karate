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
}
