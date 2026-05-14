
namespace Forge.Core.Models;

public class UpdateGageRequestModel
{
    public string? Description { get; set; }
    public string? GageType { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public int? CalibrationIntervalDays { get; set; }
    public GageStatus? Status { get; set; }
    public int? LocationId { get; set; }
    public int? AssetId { get; set; }
    public string? AccuracySpec { get; set; }
    public string? RangeSpec { get; set; }
    public string? Resolution { get; set; }
    public string? Notes { get; set; }
}
