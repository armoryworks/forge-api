using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Accounting.Determination;

/// <summary>
/// Points a configurable determination key (<see cref="ConfigurableDeterminationKeys"/>) at another account of
/// the active book. The account must be active, postable, not a control account and of the key's type. Only
/// postings made afterwards use it; posted entries are untouched. Audited as <c>GlDeterminationRuleChanged</c>.
/// </summary>
public record UpdateDeterminationRuleCommand(string Key, int GlAccountId) : IRequest<DeterminationRuleModel>;

public class UpdateDeterminationRuleValidator : AbstractValidator<UpdateDeterminationRuleCommand>
{
    public UpdateDeterminationRuleValidator()
    {
        RuleFor(x => x.Key)
            .Must(k => ConfigurableDeterminationKeys.RequiredType.ContainsKey(k))
            .WithMessage("This account mapping is managed by the system and can't be changed.")
            .WithErrorCode("determination-key-not-configurable");
        RuleFor(x => x.GlAccountId).GreaterThan(0);
    }
}

public class UpdateDeterminationRuleHandler(AppDbContext db, ISystemAuditWriter? auditWriter = null)
    : IRequestHandler<UpdateDeterminationRuleCommand, DeterminationRuleModel>
{
    public async Task<DeterminationRuleModel> Handle(UpdateDeterminationRuleCommand request, CancellationToken ct)
    {
        var book = await db.Books.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No active accounting book.");

        var requiredType = ConfigurableDeterminationKeys.RequiredType[request.Key];
        var account = await db.GlAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.GlAccountId && a.BookId == book.Id, ct)
            ?? throw Fail("GlAccountId", "determination-account-not-found", "Choose an account from this book's chart of accounts.");
        if (!account.IsActive || !account.IsPostable)
            throw Fail("GlAccountId", "determination-account-not-postable", "Choose an active account that accepts postings.");
        if (account.IsControlAccount)
            throw Fail("GlAccountId", "determination-account-control", "Control accounts can't be mapped here.");
        if (account.AccountType != requiredType)
            throw Fail("GlAccountId", "determination-account-type",
                $"{request.Key} needs an account of type {requiredType}; {account.AccountNumber} is {account.AccountType}.");

        var rule = await db.AccountDeterminationRules
            .FirstOrDefaultAsync(r => r.BookId == book.Id && r.Key == request.Key
                && r.ItemId == null && r.CategoryId == null && r.ValuationClassId == null, ct);
        var previousAccountId = rule?.GlAccountId;
        if (rule is null)
        {
            rule = new AccountDeterminationRule { BookId = book.Id, Key = request.Key, GlAccountId = account.Id };
            db.AccountDeterminationRules.Add(rule);
        }
        else
        {
            rule.GlAccountId = account.Id;
        }

        if (previousAccountId != account.Id)
            db.LogActivityAt("updated",
                $"Mapped {request.Key} to GL account {account.AccountNumber} — {account.Name}",
                ("GlAccount", account.Id));
        await db.SaveChangesAsync(ct);

        if (auditWriter is not null && previousAccountId != account.Id)
            await auditWriter.WriteAsync(
                action: "GlDeterminationRuleChanged",
                userId: db.CurrentUserId ?? 0,
                entityType: nameof(AccountDeterminationRule),
                entityId: rule.Id,
                details: JsonSerializer.Serialize(new
                {
                    key = request.Key,
                    bookId = book.Id,
                    before = previousAccountId,
                    after = account.Id,
                }),
                ct: ct);

        return new DeterminationRuleModel(
            request.Key, account.Id, account.AccountNumber, account.Name, true, requiredType.ToString());
    }

    private static ValidationException Fail(string property, string code, string message)
        => new(message, [new FluentValidation.Results.ValidationFailure(property, message) { ErrorCode = code }]);
}
