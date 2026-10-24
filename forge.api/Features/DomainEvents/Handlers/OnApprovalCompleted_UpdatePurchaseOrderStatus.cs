using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.DomainEvents.Handlers;

/// <summary>
/// Releases a purchase order held in <see cref="PurchaseOrderStatus.PendingApproval"/> once its approval
/// workflow reaches a terminal decision: an approval submits it to the vendor, a rejection returns it to Draft
/// with the reviewer's comments on its activity log. Purchase orders in any other status are left alone.
/// </summary>
public class OnApprovalCompleted_UpdatePurchaseOrderStatus(AppDbContext db, IClock clock)
    : INotificationHandler<ApprovalCompletedEvent>
{
    public async Task Handle(ApprovalCompletedEvent notification, CancellationToken ct)
    {
        if (!string.Equals(notification.EntityType, "PurchaseOrder", StringComparison.Ordinal))
            return;

        var po = await db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == notification.EntityId, ct);
        if (po is null || po.Status != PurchaseOrderStatus.PendingApproval)
            return;

        if (notification.Approved)
        {
            PurchaseOrderSubmission.MarkSubmitted(po, clock.UtcNow, db);
        }
        else
        {
            po.Status = PurchaseOrderStatus.Draft;
            var notes = notification.Notes?.Trim() ?? "";
            var reason = notes.Length == 0 ? "" : $": {(notes.Length > 200 ? notes[..200] : notes)}";
            db.LogActivityAt("approval-rejected", $"Approval rejected, returned to Draft{reason}", ("PurchaseOrder", po.Id));
        }

        await db.SaveChangesAsync(ct);
    }
}
