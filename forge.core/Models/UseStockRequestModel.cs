namespace Forge.Core.Models;

/// <summary>
/// Friendly stock-out for a standalone inventory shop — consume a quantity of a
/// part without a shipment or job issue. LocationId is optional: when omitted
/// (single-location mode) the default location is used. The amount used can never
/// drop on-hand below what is reserved (S-RI1).
/// </summary>
public record UseStockRequestModel(
    int PartId,
    int? LocationId,
    decimal Quantity,
    string? Reason,
    string? Notes);
