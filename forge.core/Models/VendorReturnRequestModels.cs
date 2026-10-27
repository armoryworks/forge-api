namespace Forge.Core.Models;

/// <summary>Ships a return to vendor: the held stock leaves inventory on <c>ShipDate</c> (default today).</summary>
public record ShipVendorReturnRequestModel(
    DateTimeOffset? ShipDate = null,
    string? Carrier = null,
    string? TrackingNumber = null,
    string? RmaNumber = null,
    string? Notes = null);

/// <summary>
/// Records the vendor's credit for a shipped return: issues a vendor credit memo for the returned lines, by default at
/// the purchase price. A line can lower the price the vendor credited.
/// </summary>
public record RecordVendorReturnCreditRequestModel(
    string? VendorReference = null,
    DateTimeOffset? MemoDate = null,
    string? Notes = null,
    List<VendorReturnCreditLineRequestModel>? Lines = null);

public record VendorReturnCreditLineRequestModel(int VendorReturnLineId, decimal? Quantity = null, decimal? UnitPrice = null);
