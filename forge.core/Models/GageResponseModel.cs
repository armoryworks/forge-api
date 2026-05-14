
namespace Forge.Core.Models;

public record GageResponseModel(
    int Id,
    string GageNumber,
    string Description,
    string? GageType,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    int CalibrationIntervalDays,
    DateTimeOffset? LastCalibratedAt,
    DateOnly? NextCalibrationDue,
    GageStatus Status,
    int? LocationId,
    string? LocationName,
    int? AssetId,
    string? AssetName,
    string? AccuracySpec,
    string? RangeSpec,
    string? Resolution,
    string? Notes,
    DateTimeOffset CreatedAt,
    int CalibrationCount);
