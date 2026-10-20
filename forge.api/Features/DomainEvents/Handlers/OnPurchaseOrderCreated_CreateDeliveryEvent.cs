using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Data;
using Forge.Api.Features.Terminology;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnPurchaseOrderCreated_CreateDeliveryEvent(
    AppDbContext db,
    ITerminologyLabels terminology,
    ILogger<OnPurchaseOrderCreated_CreateDeliveryEvent> logger)
    : INotificationHandler<PurchaseOrderCreatedEvent>
{
    public async Task Handle(PurchaseOrderCreatedEvent notification, CancellationToken ct)
    {
        var po = await db.PurchaseOrders
            .Include(p => p.Vendor)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == notification.PurchaseOrderId, ct);

        if (po is null)
        {
            logger.LogWarning("PurchaseOrder {Id} not found for delivery event creation", notification.PurchaseOrderId);
            return;
        }

        if (!po.ExpectedDeliveryDate.HasValue)
        {
            logger.LogInformation("PO {PONumber} has no expected delivery date — skipping calendar event creation", po.PONumber);
            return;
        }

        var eventTypeId = await db.CalendarEventTypes
            .Where(t => t.Key == SeedData.PoDeliveryEventTypeKey)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(ct);

        var terms = await terminology.GetAsync(ct);
        var title = $"{po.PONumber} Expected Delivery from {po.Vendor.CompanyName}";

        db.Events.Add(new Event
        {
            Title = title.Length > 200 ? title[..200] : title,
            Description = $"Expected delivery for {terms.Singular("purchase_order")} {po.PONumber} from {po.Vendor.CompanyName}.",
            StartTime = po.ExpectedDeliveryDate.Value,
            EndTime = po.ExpectedDeliveryDate.Value,
            EventType = EventType.Other,
            EventTypeId = eventTypeId,
            IsAllDay = true,
            IsSystemGenerated = true,
            CreatedByUserId = notification.UserId,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created delivery calendar event for PO {PONumber}", po.PONumber);
    }
}
