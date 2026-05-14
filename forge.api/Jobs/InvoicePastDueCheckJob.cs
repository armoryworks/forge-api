using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.DomainEvents;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

/// <summary>
/// Daily Hangfire job — finds invoices past due at 30/60/90 day intervals
/// and publishes InvoicePastDueEvent for each.
/// </summary>
public class InvoicePastDueCheckJob(
    AppDbContext db,
    IClock clock,
    IPublisher publisher,
    ILogger<InvoicePastDueCheckJob> logger)
{
    private static readonly int[] AgingBuckets = [30, 60, 90];

    public async Task Execute(CancellationToken ct)
    {
        var now = clock.UtcNow;

        var overdueInvoices = await db.Invoices
            .Where(i => i.Status != InvoiceStatus.Paid && i.Status != InvoiceStatus.Voided)
            .Where(i => i.DueDate < now)
            .Select(i => new { i.Id, i.CustomerId, i.DueDate, i.InvoiceNumber })
            .AsNoTracking()
            .ToListAsync(ct);

        var eventCount = 0;
        foreach (var invoice in overdueInvoices)
        {
            var daysOverdue = (int)(now - invoice.DueDate).TotalDays;

            // Find the highest applicable bucket
            var bucket = AgingBuckets
                .Where(b => daysOverdue >= b)
                .OrderByDescending(b => b)
                .FirstOrDefault();

            if (bucket == 0) continue;

            // Only publish at bucket boundaries (within 1 day of crossing)
            var daysSinceBucket = daysOverdue - bucket;
            if (daysSinceBucket > 1) continue;

            logger.LogInformation(
                "Invoice {InvoiceNumber} is {Days} days overdue (bucket: {Bucket})",
                invoice.InvoiceNumber, daysOverdue, bucket);

            await publisher.Publish(
                new InvoicePastDueEvent(invoice.Id, invoice.CustomerId, daysOverdue), ct);

            eventCount++;
        }

        logger.LogInformation("[InvoicePastDueCheck] Published {Count} past-due events", eventCount);
    }
}
