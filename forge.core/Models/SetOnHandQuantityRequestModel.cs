namespace Forge.Core.Models;

/// <summary>
/// Manual inventory override (forge-api#4) — directly set the on-hand quantity
/// of an existing part at a location, bypassing the receiving pipeline. PO +
/// vendor references are optional provenance; the operation succeeds without them.
/// LocationId is optional: when omitted (single-location mode, where the Locations
/// sub-feature is off), the default location is used.
/// </summary>
public record SetOnHandQuantityRequestModel(
    int PartId,
    int? LocationId,
    decimal Quantity,
    string Reason,
    string? Notes,
    int? SourcePurchaseOrderId,
    int? VendorId);
