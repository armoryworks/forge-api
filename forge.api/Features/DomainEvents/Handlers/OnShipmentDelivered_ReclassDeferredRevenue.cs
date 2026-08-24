using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

/// <summary>
/// The delivery trigger for deferred revenue (§8.4 / matrix row 2): when a shipment is
/// delivered, every invoice pinned to it that was finalized BEFORE delivery has its
/// DEFERRED_REVENUE reclassified to SALES_REVENUE and its deferred COGS / finished-goods
/// relief posted. The posting service no-ops per invoice while CAP-ACCT-FULLGL is off,
/// for straight-to-revenue invoices, and on replays (idempotent purposes), so this
/// reaction is safe to run unconditionally.
/// </summary>
public class OnShipmentDelivered_ReclassDeferredRevenue(
    AppDbContext db,
    IInvoiceArPostingService arPosting)
    : INotificationHandler<ShipmentDeliveredEvent>
{
    public async Task Handle(ShipmentDeliveredEvent notification, CancellationToken ct)
    {
        var invoiceIds = await db.Invoices.AsNoTracking()
            .Where(i => i.ShipmentId == notification.ShipmentId)
            .Select(i => i.Id)
            .ToListAsync(ct);

        foreach (var invoiceId in invoiceIds)
            await arPosting.PostDeliveryReclassAsync(invoiceId, notification.UserId, ct);
    }
}
