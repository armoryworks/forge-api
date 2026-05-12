using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnQuoteExpiring_CreateFollowUp(
    AppDbContext db,
    IClock clock,
    ILogger<OnQuoteExpiring_CreateFollowUp> logger)
    : INotificationHandler<QuoteExpiringEvent>
{
    public async Task Handle(QuoteExpiringEvent notification, CancellationToken ct)
    {
        var quote = await db.Quotes
            .Include(q => q.Customer)
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == notification.QuoteId, ct);

        if (quote is null) return;

        // Check for existing follow-up
        var exists = await db.FollowUpTasks
            .AnyAsync(f =>
                f.SourceEntityType == "Quote" &&
                f.SourceEntityId == notification.QuoteId &&
                f.TriggerType == FollowUpTriggerType.QuoteExpiring &&
                f.Status == FollowUpStatus.Open, ct);

        if (exists) return;

        // Assign to the quote's assigned salesperson, or fall back to PM/Office Manager
        var assigneeId = notification.AssignedUserId ?? 0;
        if (assigneeId == 0)
        {
            assigneeId = await db.UserRoles
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
                .Where(x => x.Name == "PM" || x.Name == "OfficeManager" || x.Name == "Admin")
                .Select(x => x.UserId)
                .FirstOrDefaultAsync(ct);
        }

        if (assigneeId == 0) return;

        var customerName = quote.Customer?.Name ?? "Unknown";
        var quoteRef = quote.QuoteNumber ?? quote.Title ?? $"#{quote.Id}";

        db.FollowUpTasks.Add(new FollowUpTask
        {
            Title = $"Quote {quoteRef} expiring in {notification.DaysUntilExpiry} day(s)",
            Description = $"Quote {quoteRef} for {customerName} expires in {notification.DaysUntilExpiry} day(s). Follow up with the customer.",
            AssignedToUserId = assigneeId,
            DueDate = quote.ExpirationDate ?? clock.UtcNow.AddDays(notification.DaysUntilExpiry),
            SourceEntityType = "Quote",
            SourceEntityId = notification.QuoteId,
            TriggerType = FollowUpTriggerType.QuoteExpiring,
            Status = FollowUpStatus.Open,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created expiring follow-up for quote {QuoteRef}", quoteRef);
    }
}
