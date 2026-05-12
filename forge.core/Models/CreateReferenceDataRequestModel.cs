namespace Forge.Core.Models;

public record CreateReferenceDataRequestModel(
    string GroupCode,
    string Code,
    string Label,
    int SortOrder,
    string? Metadata);
