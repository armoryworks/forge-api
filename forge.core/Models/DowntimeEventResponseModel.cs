
namespace Forge.Core.Models;

public record DowntimeEventResponseModel(
    int Id,
    int AssetId,
    int? WorkCenterId,
    string WorkCenterName,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    decimal? DurationMinutes,
    DowntimeCategory? Category,
    string? ReasonName,
    string? Description,
    bool IsPlanned,
    int? JobId,
    string? JobNumber,
    string? ReportedByName);
