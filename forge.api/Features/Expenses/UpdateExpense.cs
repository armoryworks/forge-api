using System.Security.Claims;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Expenses;

public record UpdateExpenseCommand(int Id, UpdateExpenseRequestModel Data) : IRequest<ExpenseResponseModel>;

public class UpdateExpenseCommandValidator : AbstractValidator<UpdateExpenseCommand>
{
    public UpdateExpenseCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.Amount).GreaterThan(0).WithMessage("Amount must be greater than zero.");
        RuleFor(x => x.Data.Category).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Data.Description).NotEmpty().MaximumLength(500);
    }
}

public class UpdateExpenseHandler(
    IExpenseRepository repo,
    IFileRepository fileRepo,
    IApprovalService approvalService,
    IMediator mediator,
    IHttpContextAccessor httpContext) : IRequestHandler<UpdateExpenseCommand, ExpenseResponseModel>
{
    public async Task<ExpenseResponseModel> Handle(UpdateExpenseCommand request, CancellationToken cancellationToken)
    {
        var expense = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Expense not found.");

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (expense.UserId != userId)
            throw new UnauthorizedAccessException("Only the submitter may edit this expense.");
        if (expense.Status is not (ExpenseStatus.Pending or ExpenseStatus.NeedsRevision))
            throw new InvalidOperationException("Only pending or needs-revision expenses may be edited.");

        var data = request.Data;
        var settings = await mediator.Send(new GetExpenseSettingsQuery(), cancellationToken);
        ExpensePolicyEnforcement.Enforce(data.Amount, data.Description, data.ReceiptFileId, settings);

        expense.Amount = data.Amount;
        expense.Category = data.Category.Trim();
        expense.Description = data.Description.Trim();
        expense.JobId = data.JobId;
        expense.ReceiptFileId = data.ReceiptFileId;
        expense.ExpenseDate = data.ExpenseDate;

        // Resubmission: flip status + clear previous reviewer state, re-submit to approval pipeline.
        var wasRevision = expense.Status == ExpenseStatus.NeedsRevision;
        if (wasRevision)
        {
            expense.Status = ExpenseStatus.Pending;
            expense.ApprovedBy = null;
            expense.ApprovalNotes = null;
        }

        await repo.SaveChangesAsync(cancellationToken);

        // Re-associate a pre-uploaded receipt (EntityId=0) with this expense row.
        if (int.TryParse(data.ReceiptFileId, out var receiptId) && receiptId > 0)
        {
            var attachment = await fileRepo.FindAsync(receiptId, cancellationToken);
            if (attachment is not null && attachment.EntityType == "expenses" && attachment.EntityId == 0)
            {
                attachment.EntityId = expense.Id;
                await fileRepo.SaveChangesAsync(cancellationToken);
            }
        }

        if (wasRevision)
        {
            await approvalService.SubmitForApprovalAsync(
                "Expense", expense.Id, userId, expense.Amount,
                $"{expense.Category} — {expense.Description}", cancellationToken);
        }

        return (await repo.GetByIdAsync(expense.Id, cancellationToken))!;
    }
}
