namespace Forge.Core.Models;

public record ReferenceDataResponseModel(
    int Id,
    string Code,
    string Label,
    int SortOrder,
    bool IsActive,
    bool IsSeedData,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string? Metadata,
    int? ParentId = null);
