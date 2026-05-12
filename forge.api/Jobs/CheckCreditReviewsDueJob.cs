using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

public class CheckCreditReviewsDueJob(
    AppDbContext db,
    IClock clock,
    ILogger<CheckCreditReviewsDueJob> logger)
{
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        var dueCustomers = await db.Customers
            .AsNoTracking()
            .Where(c => c.CreditReviewFrequencyDays != null
                && c.LastCreditReviewDate != null
                && c.LastCreditReviewDate.Value.AddDays(c.CreditReviewFrequencyDays.Value) < now)
            .Select(c => new { c.Id, c.Name, c.LastCreditReviewDate, c.CreditReviewFrequencyDays })
            .ToListAsync(ct);

        if (dueCustomers.Count == 0)
        {
            logger.LogInformation("No customers due for credit review");
            return;
        }

        foreach (var customer in dueCustomers)
        {
            logger.LogWarning(
                "Credit review overdue for customer {CustomerId} ({CustomerName}). Last review: {LastReview}, frequency: {FrequencyDays} days",
                customer.Id,
                customer.Name,
                customer.LastCreditReviewDate,
                customer.CreditReviewFrequencyDays);
        }

        logger.LogInformation("{Count} customers are due for credit review", dueCustomers.Count);
    }
}
