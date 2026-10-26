using Forge.Core.Entities;
using Forge.Core.Enums;
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

    public static void EnsureReceivable(PurchaseOrder po)
    {
        if (!ReceivableStatuses.Contains(po.Status))
            throw new InvalidOperationException(
                $"Cannot receive items on a purchase order in status {po.Status}. " +
                "Allowed: Submitted, Acknowledged, PartiallyReceived.");
    }
}
