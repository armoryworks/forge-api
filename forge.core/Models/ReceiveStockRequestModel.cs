namespace Forge.Core.Models;

/// <summary>
/// Friendly stock-in for a standalone inventory shop — add a quantity of a part
/// to stock without a purchase order. The Locations sub-feature is optional, so
/// LocationId is too: when omitted (single-location mode) the default location is
/// used. Reason is optional because the movement is self-describing as a receipt;
/// LotNumber is captured when supplied.
/// </summary>
public record ReceiveStockRequestModel(
    int PartId,
    int? LocationId,
    decimal Quantity,
    string? Reason,
    string? Notes,
    string? LotNumber);
