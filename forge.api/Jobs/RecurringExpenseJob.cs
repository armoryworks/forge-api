using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

public class RecurringExpenseJob(
    AppDbContext db,
    IClock clock,
    ILogger<RecurringExpenseJob> logger)
{
    public async Task GenerateDueExpensesAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var dueExpenses = await db.RecurringExpenses
            .Where(r => r.IsActive && r.NextOccurrenceDate <= now)
            .Where(r => r.EndDate == null || r.EndDate > now)
            .ToListAsync(ct);

        if (dueExpenses.Count == 0)
        {
            logger.LogInformation("No recurring expenses due for generation");
            return;
        }

        foreach (var recurring in dueExpenses)
        {
            var expense = new Core.Entities.Expense
            {
                UserId = recurring.UserId,
                Amount = recurring.Amount,
                Category = recurring.Category,
                Description = $"[Auto] {recurring.Description}",
                ExpenseDate = recurring.NextOccurrenceDate,
                Status = recurring.AutoApprove ? ExpenseStatus.SelfApproved : ExpenseStatus.Pending,
            };

            db.Expenses.Add(expense);

            recurring.LastGeneratedDate = now;
            recurring.NextOccurrenceDate = AdvanceDate(recurring.NextOccurrenceDate, recurring.Frequency);

            logger.LogInformation(
                "Generated expense from recurring template {Description} (ID: {Id}) for user {UserId}",
                recurring.Description, recurring.Id, recurring.UserId);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Generated {Count} expenses from recurring templates", dueExpenses.Count);
    }

    private static DateTimeOffset AdvanceDate(DateTimeOffset date, RecurrenceFrequency frequency) => frequency switch
    {
        RecurrenceFrequency.Weekly => date.AddDays(7),
        RecurrenceFrequency.Biweekly => date.AddDays(14),
        RecurrenceFrequency.Monthly => date.AddMonths(1),
        RecurrenceFrequency.Quarterly => date.AddMonths(3),
        RecurrenceFrequency.Annually => date.AddYears(1),
        _ => date.AddMonths(1),
    };
}
