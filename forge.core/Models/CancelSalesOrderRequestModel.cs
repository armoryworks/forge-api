namespace Forge.Core.Models;

/// <summary>
/// Optional body for cancelling a sales order. When <see cref="FeeAmount"/> is set (&gt; 0),
/// a late-cancellation fee is recorded on the order and billed as a one-line fee invoice.
/// Omit the body (or the fee) for a plain cancellation.
/// </summary>
public record CancelSalesOrderRequestModel(decimal? FeeAmount = null, string? FeeReason = null);
