using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

public class RecurringOrderJob(
    AppDbContext db,
    IClock clock,
    ILogger<RecurringOrderJob> logger)
{
    public async Task GenerateDueOrdersAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var dueOrders = await db.RecurringOrders
            .Include(ro => ro.Lines)
            .Where(ro => ro.IsActive && ro.NextGenerationDate <= now)
            .ToListAsync(ct);

        if (dueOrders.Count == 0)
        {
            logger.LogInformation("No recurring orders due for generation");
            return;
        }

        foreach (var recurring in dueOrders)
        {
            var salesOrder = new Core.Entities.SalesOrder
            {
                OrderNumber = $"SO-AUTO-{now:yyyyMMdd}-{recurring.Id}",
                CustomerId = recurring.CustomerId,
                ShippingAddressId = recurring.ShippingAddressId,
                Status = SalesOrderStatus.Draft,
                Notes = $"Auto-generated from recurring order: {recurring.Name}",
                Lines = recurring.Lines.Select((line, idx) => new Core.Entities.SalesOrderLine
                {
                    PartId = line.PartId,
                    Description = line.Description,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    LineNumber = idx + 1
                }).ToList()
            };

            db.SalesOrders.Add(salesOrder);

            recurring.LastGeneratedDate = now;
            recurring.NextGenerationDate = now.AddDays(recurring.IntervalDays);

            logger.LogInformation(
                "Generated sales order from recurring order {Name} (ID: {Id}) for customer {CustomerId}",
                recurring.Name, recurring.Id, recurring.CustomerId);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Generated {Count} sales orders from recurring orders", dueOrders.Count);
    }
}
