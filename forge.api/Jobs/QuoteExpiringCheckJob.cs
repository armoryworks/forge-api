using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.DomainEvents;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

/// <summary>
/// Daily Hangfire job — finds quotes expiring within 7 days
/// and publishes QuoteExpiringEvent for each.
/// </summary>
public class QuoteExpiringCheckJob(
    AppDbContext db,
    IClock clock,
    IPublisher publisher,
    ILogger<QuoteExpiringCheckJob> logger)
{
    private const int DaysBeforeExpiryWarning = 7;

    public async Task Execute(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var warningThreshold = now.AddDays(DaysBeforeExpiryWarning);

        var expiringQuotes = await db.Quotes
            .Where(q => q.Type == QuoteType.Quote)
            .Where(q => q.Status == QuoteStatus.Sent || q.Status == QuoteStatus.Draft)
            .Where(q => q.ExpirationDate.HasValue)
            .Where(q => q.ExpirationDate!.Value > now && q.ExpirationDate!.Value <= warningThreshold)
            .Select(q => new { q.Id, q.ExpirationDate, q.AssignedToId, q.QuoteNumber })
            .AsNoTracking()
            .ToListAsync(ct);

        foreach (var quote in expiringQuotes)
        {
            var daysUntilExpiry = (int)(quote.ExpirationDate!.Value - now).TotalDays;

            logger.LogInformation(
                "Quote {QuoteNumber} expires in {Days} day(s)",
                quote.QuoteNumber, daysUntilExpiry);

            await publisher.Publish(
                new QuoteExpiringEvent(quote.Id, daysUntilExpiry, quote.AssignedToId), ct);
        }

        logger.LogInformation("[QuoteExpiringCheck] Published {Count} expiring-quote events", expiringQuotes.Count);
    }
}
