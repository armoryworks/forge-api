
namespace Forge.Core.Models;

public record UpdateBOMEntryRequestModel(
    decimal? Quantity,
    string? ReferenceDesignator,
    BOMSourceType? SourceType,
    int? LeadTimeDays,
    string? Notes);
