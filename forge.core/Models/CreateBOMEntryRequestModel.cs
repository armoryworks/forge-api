using Forge.Core.Enums;

namespace Forge.Core.Models;

public record CreateBOMEntryRequestModel(
    int ChildPartId,
    decimal Quantity,
    string? ReferenceDesignator,
    BOMSourceType SourceType,
    int? LeadTimeDays,
    string? Notes);
