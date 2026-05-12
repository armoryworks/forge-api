using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class Gage : BaseAuditableEntity
{
    public string GageNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? GageType { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public int CalibrationIntervalDays { get; set; } = 365;
    public DateTimeOffset? LastCalibratedAt { get; set; }
    public DateOnly? NextCalibrationDue { get; set; }
    public GageStatus Status { get; set; } = GageStatus.InService;
    public int? LocationId { get; set; }
    public int? AssetId { get; set; }
    public string? AccuracySpec { get; set; }
    public string? RangeSpec { get; set; }
    public string? Resolution { get; set; }
    public string? Notes { get; set; }

    public StorageLocation? Location { get; set; }
    public Asset? Asset { get; set; }
    public ICollection<CalibrationRecord> CalibrationRecords { get; set; } = [];
}
