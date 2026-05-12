namespace Forge.Core.Models;

public record StageRequestModel(
    string Name,
    string Code,
    int SortOrder,
    string Color,
    int? WIPLimit,
    bool IsIrreversible);
