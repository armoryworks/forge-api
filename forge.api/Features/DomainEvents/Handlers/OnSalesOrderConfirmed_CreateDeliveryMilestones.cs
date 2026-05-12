using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnSalesOrderConfirmed_CreateDeliveryMilestones(
    AppDbContext db,
    ILogger<OnSalesOrderConfirmed_CreateDeliveryMilestones> logger)
    : INotificationHandler<SalesOrderConfirmedEvent>
{
    public async Task Handle(SalesOrderConfirmedEvent notification, CancellationToken ct)
    {
        var so = await db.SalesOrders
            .Include(s => s.Lines)
                .ThenInclude(l => l.Part)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == notification.SalesOrderId, ct);

        if (so is null)
        {
            logger.LogWarning("SalesOrder {Id} not found for delivery milestone creation", notification.SalesOrderId);
            return;
        }

        if (!so.RequestedDeliveryDate.HasValue)
        {
            logger.LogInformation("SalesOrder {OrderNumber} has no requested delivery date — skipping milestone creation", so.OrderNumber);
            return;
        }

        var deliveryDate = so.RequestedDeliveryDate.Value;

        foreach (var line in so.Lines)
        {
            var partDescription = line.Part?.Description ?? line.Description;
            var title = $"SO-{so.OrderNumber} Line {line.LineNumber} Delivery: {partDescription}";

            db.Events.Add(new Event
            {
                Title = title.Length > 200 ? title[..200] : title,
                Description = $"Delivery milestone for Sales Order {so.OrderNumber}, Line {line.LineNumber}. Quantity: {line.Quantity}.",
                StartTime = deliveryDate,
                EndTime = deliveryDate,
                EventType = EventType.Other,
                IsAllDay = true,
                IsSystemGenerated = true,
                CreatedByUserId = notification.UserId,
            });
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created {Count} delivery milestone event(s) for SO {OrderNumber}", so.Lines.Count, so.OrderNumber);
    }
}
