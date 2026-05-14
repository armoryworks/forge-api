using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnInvoicePastDue_CreateFollowUp(
    AppDbContext db,
    IClock clock,
    ILogger<OnInvoicePastDue_CreateFollowUp> logger)
    : INotificationHandler<InvoicePastDueEvent>
{
    public async Task Handle(InvoicePastDueEvent notification, CancellationToken ct)
    {
        var invoice = await db.Invoices
            .Include(i => i.Customer)
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == notification.InvoiceId, ct);

        if (invoice is null) return;

        // Determine the aging bucket label
        var bucketLabel = notification.DaysOverdue switch
        {
            >= 90 => "90+ days",
            >= 60 => "60 days",
            >= 30 => "30 days",
            _ => $"{notification.DaysOverdue} days",
        };

        // Check if a follow-up already exists for this invoice at this interval
        var intervalKey = notification.DaysOverdue switch
        {
            >= 90 => "90",
            >= 60 => "60",
            >= 30 => "30",
            _ => notification.DaysOverdue.ToString(),
        };

        var existingFollowUp = await db.FollowUpTasks
            .AnyAsync(f =>
                f.SourceEntityType == "Invoice" &&
                f.SourceEntityId == notification.InvoiceId &&
                f.TriggerType == FollowUpTriggerType.InvoicePastDue &&
                f.Title.Contains(intervalKey), ct);

        if (existingFollowUp)
        {
            logger.LogDebug("Follow-up already exists for invoice {InvoiceId} at {Interval} days", notification.InvoiceId, intervalKey);
            return;
        }

        // Assign to Office Manager
        var assigneeId = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => x.Name == "OfficeManager" || x.Name == "Admin")
            .Select(x => x.UserId)
            .FirstOrDefaultAsync(ct);

        if (assigneeId == 0) return;

        var customerName = invoice.Customer?.Name ?? "Unknown";

        db.FollowUpTasks.Add(new FollowUpTask
        {
            Title = $"Invoice {invoice.InvoiceNumber} past due — {bucketLabel}",
            Description = $"Invoice {invoice.InvoiceNumber} for {customerName} is {bucketLabel} overdue. Balance due: {invoice.BalanceDue:C}.",
            AssignedToUserId = assigneeId,
            DueDate = clock.UtcNow,
            SourceEntityType = "Invoice",
            SourceEntityId = notification.InvoiceId,
            TriggerType = FollowUpTriggerType.InvoicePastDue,
            Status = FollowUpStatus.Open,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created {Bucket} past-due follow-up for invoice {InvoiceNumber}", bucketLabel, invoice.InvoiceNumber);
    }
}
