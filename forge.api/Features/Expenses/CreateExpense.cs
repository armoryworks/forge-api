using System.Security.Claims;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Forge.Api.Features.Approvals;
using Forge.Api.Features.Notifications;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Enums;
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
        RuleFor(x => x.Data.VendorId).GreaterThan(0).When(x => x.Data.VendorId is not null);
    }
}

public class CreateExpenseHandler(
    IExpenseRepository repo,
    IFileRepository fileRepo,
    IApprovalService approvalService,
    IMediator mediator,
    IHttpContextAccessor httpContext,
    IAssignmentNotifier? assignmentNotifier = null) : IRequestHandler<CreateExpenseCommand, ExpenseResponseModel>
{
    public async Task<ExpenseResponseModel> Handle(CreateExpenseCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var settings = await mediator.Send(new GetExpenseSettingsQuery(), cancellationToken);
        ExpensePolicyEnforcement.Enforce(data.Amount, data.Description, data.ReceiptFileId, settings);

        var receipt = int.TryParse(data.ReceiptFileId, out var receiptId) && receiptId > 0
            ? await fileRepo.FindAsync(receiptId, cancellationToken)
            : null;
        if (receipt is { EntityType: "expenses", EntityId: 0 } && receipt.UploadedById != userId)
            throw new ForbiddenException("That receipt was uploaded by someone else.");

        var expense = new Expense
        {
            UserId = userId,
            JobId = data.JobId,
            Amount = data.Amount,
            Category = data.Category.Trim(),
            Description = data.Description.Trim(),
            ReceiptFileId = data.ReceiptFileId,
            ExpenseDate = data.ExpenseDate,
            // Vendor-settled: a named vendor routes the expense to Accounts Payable — approval
            // promotes it into a vendor bill paid through the AP pipeline. Absent → owed to the employee.
            VendorId = data.VendorId,
            SettlementTarget = data.VendorId is not null ? ExpenseSettlementTarget.AccountsPayable : null,
        };

        await repo.AddAsync(expense, cancellationToken);

        // Re-associate a pre-uploaded receipt (EntityId=0) with the new expense row.
        if (receipt is { EntityType: "expenses", EntityId: 0 })
        {
            receipt.EntityId = expense.Id;
            await fileRepo.SaveChangesAsync(cancellationToken);
        }

        await approvalService.SubmitAndPublishAsync(
            mediator, "Expense", expense.Id, userId, expense.Amount,
            $"{expense.Category} — {expense.Description}", cancellationToken, assignmentNotifier);

        return (await repo.GetByIdAsync(expense.Id, cancellationToken))!;
    }
}
