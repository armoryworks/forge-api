using Forge.Core.Enums;

namespace Forge.Core.Models;

public record CreateBOMEntryRequestModel(
    int ChildPartId,
    decimal Quantity,
    string? ReferenceDesignator,
    BOMSourceType SourceType,
    int? LeadTimeDays,
    string? Notes,
    // UoM purchase-options effort — the UoM this component is consumed in (default = child's stock UoM).
    int? UomId = null);
