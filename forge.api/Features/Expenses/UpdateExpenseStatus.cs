using System.Security.Claims;
using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Expenses;

public record UpdateExpenseStatusCommand(int Id, UpdateExpenseStatusRequestModel Data) : IRequest<ExpenseResponseModel>;

public class UpdateExpenseStatusValidator : AbstractValidator<UpdateExpenseStatusCommand>
{
    private const int DeclineNoteMinLength = 10;

    public UpdateExpenseStatusValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.Status).IsInEnum();
        RuleFor(x => x.Data.ApprovalNotes).MaximumLength(1000).When(x => x.Data.ApprovalNotes is not null);
        RuleFor(x => x.Data.ApprovalNotes)
            .NotEmpty()
            .MinimumLength(DeclineNoteMinLength)
            .When(x => x.Data.Status is ExpenseStatus.Rejected or ExpenseStatus.NeedsRevision)
            .WithMessage($"A note of at least {DeclineNoteMinLength} characters is required when rejecting or requesting revision.");
    }
}

public class UpdateExpenseStatusHandler(
    IExpenseRepository repo,
    IHttpContextAccessor httpContext,
    ISyncQueueRepository syncQueue,
    IAccountingProviderFactory providerFactory,
    ILogger<UpdateExpenseStatusHandler> logger) : IRequestHandler<UpdateExpenseStatusCommand, ExpenseResponseModel>
{
    public async Task<ExpenseResponseModel> Handle(UpdateExpenseStatusCommand request, CancellationToken cancellationToken)
    {
        var expense = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Expense not found.");

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        expense.Status = request.Data.Status;
        expense.ApprovedBy = userId;
        expense.ApprovalNotes = request.Data.ApprovalNotes?.Trim();

        await repo.SaveChangesAsync(cancellationToken);

        // Enqueue QB expense creation when approved
        if (request.Data.Status is ExpenseStatus.Approved or ExpenseStatus.SelfApproved)
        {
            try
            {
                var accountingService = await providerFactory.GetActiveProviderAsync(cancellationToken);
                if (accountingService is not null)
                {
                    var syncStatus = await accountingService.GetSyncStatusAsync(cancellationToken);
                    if (syncStatus.Connected)
                    {
                        var accountingExpense = new AccountingExpense(
                            VendorExternalId: null,
                            CustomerExternalId: null,
                            Amount: expense.Amount,
                            Date: expense.ExpenseDate,
                            Description: expense.Description,
                            Category: expense.Category,
                            RefNumber: $"EXP-{expense.Id}");
                        var payload = JsonSerializer.Serialize(accountingExpense);
                        await syncQueue.EnqueueAsync("Expense", expense.Id, "CreateExpense", payload, cancellationToken);
                        logger.LogInformation("Enqueued CreateExpense sync for Expense {ExpenseId}", expense.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to enqueue expense sync for Expense {ExpenseId} — continuing", expense.Id);
            }
        }

        return (await repo.GetByIdAsync(expense.Id, cancellationToken))!;
    }
}
