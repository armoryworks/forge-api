using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
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
            .Include(i => i.Lines)
            .Include(i => i.PaymentApplications)
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

        var title = $"Invoice {invoice.InvoiceNumber} past due — {bucketLabel}";

        var existingFollowUp = await db.FollowUpTasks
            .AnyAsync(f =>
                f.SourceEntityType == "Invoice" &&
                f.SourceEntityId == notification.InvoiceId &&
                f.TriggerType == FollowUpTriggerType.InvoicePastDue &&
                f.Title == title, ct);

        if (existingFollowUp)
        {
            logger.LogDebug("Follow-up already exists for invoice {InvoiceId} at {Bucket}", notification.InvoiceId, bucketLabel);
            return;
        }

        // Assign to Office Manager
        var assigneeId = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Join(db.Users, x => x.UserId, u => u.Id, (x, u) => new { x.UserId, x.Name, u.IsActive })
            .Where(x => x.IsActive && (x.Name == "OfficeManager" || x.Name == "Admin"))
            .OrderBy(x => x.Name == "OfficeManager" ? 0 : 1)
            .ThenBy(x => x.UserId)
            .Select(x => x.UserId)
            .FirstOrDefaultAsync(ct);

        if (assigneeId == 0) return;

        var customerName = invoice.Customer?.Name ?? "Unknown";
        var balance = (await MoneyFormat.ForCurrencyIdAsync(db, invoice.CurrencyId, ct)).Format(invoice.BalanceDue);

        db.FollowUpTasks.Add(new FollowUpTask
        {
            Title = title,
            Description = $"Invoice {invoice.InvoiceNumber} for {customerName} is {bucketLabel} overdue. Balance due: {balance}.",
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
