using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PurchaseOrders;

public record CancelPurchaseOrderCommand(int Id) : IRequest;

public class CancelPurchaseOrderHandler(IPurchaseOrderRepository repo, AppDbContext db, IClock clock)
    : IRequestHandler<CancelPurchaseOrderCommand>
{
    public async Task Handle(CancelPurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var po = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order {request.Id} not found");

        // F-033: source-state whitelist — only pre-receipt POs may be cancelled;
        // re-cancel of Cancelled is a silent duplicate; PartiallyReceived/Received/Closed have committed stock
        if (po.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.PendingApproval or PurchaseOrderStatus.Submitted or PurchaseOrderStatus.Acknowledged))
            throw new InvalidOperationException(
                $"Cannot cancel a purchase order in status {po.Status}. Allowed: Draft, PendingApproval, Submitted, Acknowledged.");

        var previousStatus = po.Status;
        po.Status = PurchaseOrderStatus.Cancelled;
        if (previousStatus == PurchaseOrderStatus.PendingApproval)
        {
            var openApprovals = await db.ApprovalRequests
                .Where(r => r.EntityType == "PurchaseOrder"
                    && r.EntityId == po.Id
                    && (r.Status == ApprovalRequestStatus.Pending || r.Status == ApprovalRequestStatus.Escalated))
                .ToListAsync(cancellationToken);
            foreach (var approval in openApprovals)
            {
                approval.Status = ApprovalRequestStatus.Cancelled;
                approval.CompletedAt = clock.UtcNow;
            }
        }

        db.LogActivityAt("cancelled", $"Cancelled (was {previousStatus})", ("PurchaseOrder", po.Id));
        await repo.SaveChangesAsync(cancellationToken);
    }
}
