using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Features.DomainEvents;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.PurchaseOrders;

// Phase 3 / WU-14 / H3 — short-close a partially-received PO. Standard ERP
// practice: the remaining (unreceived) qty is marked as cancelled-not-received
// (NOT received), the line stays for historical accuracy, the PO is closed.
// Without this the close endpoint 409s on partial POs and they cluttered the
// AP queue indefinitely.
public record ShortClosePurchaseOrderCommand(int Id, string Reason) : IRequest<int>;

public class ShortClosePurchaseOrderHandler(
    IPurchaseOrderRepository repo,
    IMediator mediator,
    IHttpContextAccessor httpContext)
    : IRequestHandler<ShortClosePurchaseOrderCommand, int>
{
    public async Task<int> Handle(ShortClosePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("Short-close reason is required.", nameof(request));

        var po = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order {request.Id} not found");

        // Gate: PO must not already be Draft (not yet issued), Closed, or
        // Cancelled. There must be at least one line where received < ordered
        // (i.e. something to short-close).
        if (po.Status == PurchaseOrderStatus.Draft)
            throw new InvalidOperationException("Cannot short-close a Draft PO. Submit it first or cancel.");
        if (po.Status == PurchaseOrderStatus.Closed)
            throw new InvalidOperationException("PO is already closed.");
        if (po.Status == PurchaseOrderStatus.Cancelled)
            throw new InvalidOperationException("Cannot short-close a cancelled PO.");

        var hasUnreceived = po.Lines.Any(l => l.OrderedQuantity > l.ReceivedQuantity + l.CancelledShortCloseQuantity);
        if (!hasUnreceived)
            throw new InvalidOperationException("Nothing to short-close — all lines fully received.");

        // Mark each line's unreceived qty as cancelled_short_close. Leave
        // ReceivedQuantity intact for historical accuracy.
        decimal totalCancelled = 0m;
        foreach (var line in po.Lines)
        {
            var unreceived = line.OrderedQuantity - line.ReceivedQuantity - line.CancelledShortCloseQuantity;
            if (unreceived > 0)
            {
                line.CancelledShortCloseQuantity += unreceived;
                totalCancelled += unreceived;
            }
        }

        po.Status = PurchaseOrderStatus.Closed;
        po.ShortCloseReason = request.Reason.Trim();
        po.ShortClosedAt = DateTimeOffset.UtcNow;

        await repo.SaveChangesAsync(cancellationToken);

        // Audit pickup via the WU-03 domain-event audit handler. GR/IR
        // variance posting handled downstream by the accounting service if
        // wired (out of scope for this WU — emit and let the listener decide).
        var claim = httpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userId = int.TryParse(claim, out var u) ? u : 0;
        await mediator.Publish(
            new PurchaseOrderShortClosedEvent(po.Id, userId, po.ShortCloseReason, totalCancelled),
            cancellationToken);

        return po.Id;
    }
}
