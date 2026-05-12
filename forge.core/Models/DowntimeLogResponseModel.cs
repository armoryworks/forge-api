namespace Forge.Core.Models;

public record DowntimeLogResponseModel(
    int Id,
    int AssetId,
    string AssetName,
    int? ReportedById,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string Reason,
    string? Resolution,
    bool IsPlanned,
    string? Notes,
    decimal DurationHours,
    DateTimeOffset CreatedAt);
