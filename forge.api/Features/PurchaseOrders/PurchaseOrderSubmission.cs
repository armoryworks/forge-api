using System.Globalization;

using Forge.Api.Features.Tolerances;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PurchaseOrders;

public static class PurchaseOrderSubmission
{
    public static readonly PurchaseOrderStatus[] ReceivableStatuses =
    [
        PurchaseOrderStatus.Submitted,
        PurchaseOrderStatus.Acknowledged,
        PurchaseOrderStatus.PartiallyReceived,
    ];

    public static void MarkSubmitted(PurchaseOrder po, DateTimeOffset now, AppDbContext db)
    {
        po.Status = PurchaseOrderStatus.Submitted;
        po.SubmittedDate = now;
        db.LogActivityAt("submitted", "Submitted to vendor", ("PurchaseOrder", po.Id));
    }

    public static void EnsureReceivable(
        PurchaseOrder po, IEnumerable<PurchaseOrderLine> lines, Func<PurchaseOrderLine, QuantityTolerance> toleranceFor)
    {
        if (ReceivableStatuses.Contains(po.Status))
            return;
        var received = lines.ToList();
        if (po.Status == PurchaseOrderStatus.Received
            && received.Count > 0
            && received.All(line => HasRoomWithinTolerance(line, toleranceFor(line))))
            return;
        throw new InvalidOperationException(
            $"Cannot receive items on a purchase order in status {po.Status}. " +
            "Allowed: Submitted, Acknowledged, PartiallyReceived.");
    }

    public static bool HasRoomWithinTolerance(PurchaseOrderLine line, QuantityTolerance tolerance)
        => line.ReceivedQuantity < tolerance.MaxAccepted(PurchaseLineTolerance.OrderedBasis(line));

    public static Task LockForReceiptAsync(AppDbContext db, int purchaseOrderId, CancellationToken ct)
        => db.AcquireAdvisoryLockAsync(
            "purchase-orders.receive", purchaseOrderId.ToString(CultureInfo.InvariantCulture), ct);
}
