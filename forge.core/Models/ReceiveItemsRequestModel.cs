namespace Forge.Core.Models;

// Bought-parts effort PR3 — receipt-level freight capture. ActualFreight
// + AllocationMethod live at the request top so a single receipt can split
// a freight invoice across the lines being received in one call. Both are
// optional: when ActualFreight is null, the handler defaults from the PO's
// EstimatedFreight; null-after-default is a "freight not yet captured"
// receipt and AllocatedFreight stays null per line.
public record ReceiveItemsRequestModel(
    List<ReceiveLineModel> Lines,
    decimal? ActualFreight = null,
    Enums.FreightAllocationMethod FreightAllocationMethod = Enums.FreightAllocationMethod.ByExtendedValue);

// ManualFreight populated only when AllocationMethod is Manual; ignored otherwise.
public record ReceiveLineModel(
    int LineId,
    decimal Quantity,
    int? StorageLocationId,
    string? Notes,
    decimal? ManualFreight = null);
