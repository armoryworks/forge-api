namespace Forge.Core.Models;

public record TimeCorrectionLogResponseModel(
    int Id,
    int TimeEntryId,
    int CorrectedByUserId,
    string CorrectedByName,
    string Reason,
    int? OriginalJobId,
    string? OriginalJobNumber,
    DateOnly OriginalDate,
    int OriginalDurationMinutes,
    DateTimeOffset? OriginalStartTime,
    DateTimeOffset? OriginalEndTime,
    string? OriginalCategory,
    string? OriginalNotes,
    DateTimeOffset CreatedAt);
