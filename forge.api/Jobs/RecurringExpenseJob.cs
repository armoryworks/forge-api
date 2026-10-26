using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Approvals;
using Forge.Api.Features.Expenses;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Jobs;

/// <summary>
/// Files the expenses that recurring templates have due, as the template's owner and through the same path as a
/// hand-filed expense: the expense starts Pending and is submitted to the approval workflow. A template marked
/// auto-approve is self-approved only when the <c>expense_self_approval</c> setting allows it and no approval step
/// is waiting; the approval then posts to the GL exactly like a hand-approved expense.
/// </summary>
public class RecurringExpenseJob(
    AppDbContext db,
    IClock clock,
    IMediator mediator,
    IApprovalService approvalService,
    ILogger<RecurringExpenseJob> logger)
{
    private const string AutoApprovalNote = "Approved automatically from the recurring expense template.";

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

        var allowSelfApproval = (await mediator.Send(new GetExpenseSettingsQuery(), ct)).AllowSelfApproval;

        foreach (var recurring in dueExpenses)
        {
            var expense = new Expense
            {
                UserId = recurring.UserId,
                Amount = recurring.Amount,
                Category = recurring.Category,
                Description = $"[Auto] {recurring.Description}",
                ExpenseDate = recurring.NextOccurrenceDate,
                Status = ExpenseStatus.Pending,
            };

            db.Expenses.Add(expense);
            recurring.LastGeneratedDate = now;
            recurring.NextOccurrenceDate = AdvanceDate(recurring.NextOccurrenceDate, recurring.Frequency);
            await db.SaveChangesAsync(ct);

            db.LogActivityAt("created", $"Filed from recurring expense \"{recurring.Description}\"", ("Expense", expense.Id));
            await db.SaveChangesAsync(ct);

            await SubmitAsync(recurring, expense, allowSelfApproval, ct);

            logger.LogInformation(
                "Generated expense {ExpenseId} from recurring template {Description} (ID: {Id}) for user {UserId}",
                expense.Id, recurring.Description, recurring.Id, recurring.UserId);
        }

        logger.LogInformation("Generated {Count} expenses from recurring templates", dueExpenses.Count);
    }

    private async Task SubmitAsync(RecurringExpense recurring, Expense expense, bool allowSelfApproval, CancellationToken ct)
    {
        try
        {
            var request = await approvalService.SubmitAndPublishAsync(
                mediator, "Expense", expense.Id, recurring.UserId, expense.Amount,
                $"{expense.Category} — {expense.Description}", ct);

            if (!recurring.AutoApprove || !allowSelfApproval || request is not null)
                return;

            await mediator.Send(new UpdateExpenseStatusCommand(
                expense.Id,
                new UpdateExpenseStatusRequestModel(ExpenseStatus.SelfApproved, AutoApprovalNote),
                ActorUserId: recurring.UserId), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Recurring expense {ExpenseId} was filed but its approval did not complete; it stays Pending", expense.Id);
        }
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
