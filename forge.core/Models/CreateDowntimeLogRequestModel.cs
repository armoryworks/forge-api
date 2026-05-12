using Forge.Core.Enums;

namespace Forge.Core.Models;

public record CreateDowntimeLogRequestModel(
    int AssetId,
    int? WorkCenterId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    DowntimeCategory? Category,
    int? DowntimeReasonId,
    string Reason,
    string? Resolution,
    string? Description,
    bool IsPlanned,
    int? JobId,
    string? Notes);
