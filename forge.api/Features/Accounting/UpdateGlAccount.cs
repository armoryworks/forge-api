using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Edits a chart-of-accounts account. Safe fields (name, description, active, requires-job/cost-center,
/// cash-flow category, parent) are always editable. The <b>structural</b> fields — account number, type,
/// normal balance — may only change while the account has <b>no journal postings</b>; once it carries
/// activity, changing them would corrupt the statements + the account-determination map, so they are
/// locked. Control accounts (AR/AP/Inventory) are system-managed and cannot be edited or deactivated
/// here. Deactivation only hides the account from pickers; historical postings are untouched.
/// </summary>
public record UpdateGlAccountCommand(
    int Id,
    string Name,
    int? ParentAccountId,
    bool RequiresJob,
    bool RequiresCostCenter,
    CashFlowCategory? CashFlowCategory,
    string? Description,
    bool IsActive,
    // Structural — applied only when the account has no postings.
    string? AccountNumber,
    AccountType? AccountType,
    NormalBalance? NormalBalance) : IRequest<GlAccountAdminModel>;

public class UpdateGlAccountValidator : AbstractValidator<UpdateGlAccountCommand>
{
    public UpdateGlAccountValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public class UpdateGlAccountHandler(AppDbContext db)
    : IRequestHandler<UpdateGlAccountCommand, GlAccountAdminModel>
{
    public async Task<GlAccountAdminModel> Handle(UpdateGlAccountCommand request, CancellationToken ct)
    {
        var account = await db.GlAccounts.FirstOrDefaultAsync(a => a.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"GL account {request.Id} not found.");

        if (account.IsControlAccount)
            throw new InvalidOperationException("Control accounts (AR / AP / Inventory) are system-managed and cannot be edited here.");

        var hasPostings = await db.JournalLines.AnyAsync(l => l.GlAccountId == account.Id, ct);

        // Safe fields — always editable.
        account.Name = request.Name.Trim();
        account.Description = request.Description?.Trim();
        account.ParentAccountId = request.ParentAccountId;
        account.RequiresJob = request.RequiresJob;
        account.RequiresCostCenter = request.RequiresCostCenter;
        account.CashFlowCategory = request.CashFlowCategory;
        account.IsActive = request.IsActive;

        // Structural fields — only while the account has no postings.
        var wantsStructural = request.AccountNumber is not null || request.AccountType is not null || request.NormalBalance is not null;
        if (wantsStructural)
        {
            if (hasPostings)
                throw new InvalidOperationException("This account has journal postings — its number, type, and normal balance are locked. Create a new account instead.");

            var newType = request.AccountType ?? account.AccountType;
            var newBalance = request.NormalBalance ?? account.NormalBalance;
            if (GlAccountRules.ExpectedNormalBalance(newType) != newBalance)
                throw new InvalidOperationException(
                    $"A {newType} account must have a {GlAccountRules.ExpectedNormalBalance(newType)} normal balance.");

            if (request.AccountNumber is string num)
            {
                var trimmed = num.Trim();
                if (trimmed != account.AccountNumber &&
                    await db.GlAccounts.AnyAsync(a => a.BookId == account.BookId && a.AccountNumber == trimmed && a.Id != account.Id, ct))
                    throw new InvalidOperationException($"Account number '{trimmed}' already exists in this book.");
                account.AccountNumber = trimmed;
            }
            account.AccountType = newType;
            account.NormalBalance = newBalance;
        }

        db.LogActivityAt("updated",
            $"Updated GL account {account.AccountNumber} — {account.Name}",
            ("GlAccount", account.Id));
        await db.SaveChangesAsync(ct);

        return account.ToAdminModel(hasPostings);
    }
}
