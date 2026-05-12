using System.Security.Claims;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Expenses;

public record CreateExpenseCommand(CreateExpenseRequestModel Data) : IRequest<ExpenseResponseModel>;

public class CreateExpenseCommandValidator : AbstractValidator<CreateExpenseCommand>
{
    public CreateExpenseCommandValidator()
    {
        RuleFor(x => x.Data.Amount).GreaterThan(0).WithMessage("Amount must be greater than zero.");
        RuleFor(x => x.Data.Category).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Data.Description).NotEmpty().MaximumLength(500);
    }
}

public class CreateExpenseHandler(
    IExpenseRepository repo,
    IFileRepository fileRepo,
    IApprovalService approvalService,
    IMediator mediator,
    IHttpContextAccessor httpContext) : IRequestHandler<CreateExpenseCommand, ExpenseResponseModel>
{
    public async Task<ExpenseResponseModel> Handle(CreateExpenseCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var settings = await mediator.Send(new GetExpenseSettingsQuery(), cancellationToken);
        ExpensePolicyEnforcement.Enforce(data.Amount, data.Description, data.ReceiptFileId, settings);

        var expense = new Expense
        {
            UserId = userId,
            JobId = data.JobId,
            Amount = data.Amount,
            Category = data.Category.Trim(),
            Description = data.Description.Trim(),
            ReceiptFileId = data.ReceiptFileId,
            ExpenseDate = data.ExpenseDate,
        };

        await repo.AddAsync(expense, cancellationToken);

        // Re-associate a pre-uploaded receipt (EntityId=0) with the new expense row.
        if (int.TryParse(data.ReceiptFileId, out var receiptId) && receiptId > 0)
        {
            var attachment = await fileRepo.FindAsync(receiptId, cancellationToken);
            if (attachment is not null && attachment.EntityType == "expenses" && attachment.EntityId == 0)
            {
                attachment.EntityId = expense.Id;
                await fileRepo.SaveChangesAsync(cancellationToken);
            }
        }

        await approvalService.SubmitForApprovalAsync(
            "Expense", expense.Id, userId, expense.Amount,
            $"{expense.Category} — {expense.Description}", cancellationToken);

        return (await repo.GetByIdAsync(expense.Id, cancellationToken))!;
    }
}
