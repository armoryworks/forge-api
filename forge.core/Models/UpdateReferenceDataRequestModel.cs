namespace Forge.Core.Models;

public record UpdateReferenceDataRequestModel(
    string? Label,
    int? SortOrder,
    bool? IsActive,
    string? Metadata);
