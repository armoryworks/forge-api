namespace Forge.Core.Models;

/// <summary>
/// A material spec (or a material category when <paramref name="ParentId"/> is null) as the Material specs page lists
/// it, with the number of parts that use it.
/// </summary>
public record MaterialSpecResponseModel(
    int Id,
    string Code,
    string Label,
    int? ParentId,
    string? CategoryLabel,
    int SortOrder,
    bool IsActive,
    bool IsSeedData,
    int PartCount);
