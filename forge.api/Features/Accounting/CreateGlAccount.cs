using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Creates a new chart-of-accounts account. Deliberately conservative for financial correctness:
/// only user-managed <b>postable detail accounts</b> are created here (control accounts — AR/AP/
/// Inventory — are system-seeded and never user-created), the account number must be unique within the
/// book, and the normal balance must match the account type's convention (Asset/Expense = Debit;
/// Liability/Equity/Income = Credit) so statements and the posting engine stay coherent.
/// </summary>
public record CreateGlAccountCommand(
    int BookId,
    string AccountNumber,
    string Name,
    AccountType AccountType,
    NormalBalance NormalBalance,
    int? ParentAccountId,
    bool RequiresJob,
    bool RequiresCostCenter,
    CashFlowCategory? CashFlowCategory,
    string? Description) : IRequest<GlAccountAdminModel>;

public class CreateGlAccountValidator : AbstractValidator<CreateGlAccountCommand>
{
    public CreateGlAccountValidator()
    {
        RuleFor(x => x.BookId).GreaterThan(0);
        RuleFor(x => x.AccountNumber).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public class CreateGlAccountHandler(AppDbContext db)
    : IRequestHandler<CreateGlAccountCommand, GlAccountAdminModel>
{
    public async Task<GlAccountAdminModel> Handle(CreateGlAccountCommand request, CancellationToken ct)
    {
        if (GlAccountRules.ExpectedNormalBalance(request.AccountType) != request.NormalBalance)
            throw new InvalidOperationException(
                $"A {request.AccountType} account must have a {GlAccountRules.ExpectedNormalBalance(request.AccountType)} normal balance.");

        var number = request.AccountNumber.Trim();
        if (await db.GlAccounts.AnyAsync(a => a.BookId == request.BookId && a.AccountNumber == number, ct))
            throw new InvalidOperationException($"Account number '{number}' already exists in this book.");

        if (request.ParentAccountId is int parentId &&
            !await db.GlAccounts.AnyAsync(a => a.Id == parentId && a.BookId == request.BookId, ct))
            throw new InvalidOperationException("Parent account not found in this book.");

        var account = new GlAccount
        {
            BookId = request.BookId,
            AccountNumber = number,
            Name = request.Name.Trim(),
            AccountType = request.AccountType,
            NormalBalance = request.NormalBalance,
            ParentAccountId = request.ParentAccountId,
            IsControlAccount = false,   // control accounts are system-seeded, never user-created
            ControlType = null,
            IsPostable = true,          // user-created accounts are postable detail accounts
            IsActive = true,
            RequiresJob = request.RequiresJob,
            RequiresCostCenter = request.RequiresCostCenter,
            CashFlowCategory = request.CashFlowCategory,
            Description = request.Description?.Trim(),
        };
        db.GlAccounts.Add(account);
        await db.SaveChangesAsync(ct);

        db.LogActivityAt("created",
            $"Created GL account {account.AccountNumber} — {account.Name} ({account.AccountType})",
            ("GlAccount", account.Id));
        await db.SaveChangesAsync(ct);

        return account.ToAdminModel(hasPostings: false);
    }
}
