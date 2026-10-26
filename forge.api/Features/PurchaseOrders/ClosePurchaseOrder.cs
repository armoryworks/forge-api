using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.Accounting;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PurchaseOrders;

public record ClosePurchaseOrderCommand(int Id) : IRequest;

public class ClosePurchaseOrderHandler(
    IPurchaseOrderRepository repo,
    AppDbContext db,
    IVendorBillApPostingService? apPosting = null,
    IClock? clock = null)
    : IRequestHandler<ClosePurchaseOrderCommand>
{
    public async Task Handle(ClosePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var po = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order {request.Id} not found");

        if (po.Status != PurchaseOrderStatus.Received)
            throw new InvalidOperationException("Only received purchase orders can be closed");

        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        po.Status = PurchaseOrderStatus.Closed;
        db.LogActivityAt("closed", "Closed", ("PurchaseOrder", po.Id));
        await repo.SaveChangesAsync(cancellationToken);

        if (apPosting is not null)
            await apPosting.PostPurchaseOrderFreightCloseAsync(
                po.Id, DateOnly.FromDateTime((clock ?? new Forge.Integrations.SystemClock()).UtcNow.UtcDateTime),
                db.CurrentUserId ?? 0, cancellationToken);

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);
    }
}
