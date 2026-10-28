using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.Tolerances;

/// <summary>
/// Applies the receipt tolerance to a purchase-order line. The band is measured against the ordered quantity
/// less any manual short-close. A receipt that reaches the under-receipt floor short-closes the rest "within
/// tolerance" (through <see cref="PurchaseOrderLine.CancelledShortCloseQuantity"/>, so open supply, GRNI and the
/// PO status all see the line as done); that close is recomputed when more arrives.
/// </summary>
public static class PurchaseLineTolerance
{
    public const string Kind = "purchase-receipt";

    public static decimal OrderedBasis(PurchaseOrderLine line)
        => line.OrderedQuantity - (line.ClosedWithinTolerance ? 0m : line.CancelledShortCloseQuantity);

    /// <summary>Re-evaluates the line after its received quantity changed; returns the quantity it newly short-closed.</summary>
    public static decimal ApplyAfterReceipt(PurchaseOrderLine line, QuantityTolerance tolerance)
    {
        var previouslyClosed = line.ClosedWithinTolerance ? line.CancelledShortCloseQuantity : 0m;
        if (line.ShortCloseReason == PurchaseOrderLine.WithinToleranceReason)
        {
            line.CancelledShortCloseQuantity = 0m;
            line.ShortCloseReason = null;
        }

        var basis = line.OrderedQuantity - line.CancelledShortCloseQuantity;
        var open = basis - line.ReceivedQuantity;
        if (open <= 0m || line.ReceivedQuantity <= 0m || !tolerance.Completes(basis, line.ReceivedQuantity))
            return 0m;

        line.CancelledShortCloseQuantity += open;
        line.ShortCloseReason = PurchaseOrderLine.WithinToleranceReason;
        return Math.Max(0m, open - previouslyClosed);
    }
}
