namespace Forge.Core.Models;

public record PartRevisionResponseModel(
    int Id,
    int PartId,
    string Revision,
    string? ChangeDescription,
    string? ChangeReason,
    DateTimeOffset EffectiveDate,
    bool IsCurrent,
    int FileCount,
    DateTimeOffset CreatedAt);
