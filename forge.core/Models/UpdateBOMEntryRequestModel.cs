using Forge.Core.Enums;

namespace Forge.Core.Models;

public record UpdateBOMEntryRequestModel(
    decimal? Quantity,
    string? ReferenceDesignator,
    BOMSourceType? SourceType,
    int? LeadTimeDays,
    string? Notes,
    // UoM purchase-options effort — change the component's consumption UoM (set/change only,
    // matching the other BOM fields' nullable-skip semantics).
    int? UomId = null);
