using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Read-only chart of accounts for a book (DARK behind <c>CAP-ACCT-FULLGL</c>) — the pick-list behind
/// the manual journal-entry editor and other GL surfaces. Active accounts only, ordered by number;
/// <c>PostableOnly</c> narrows to postable, non-control accounts (control accounts post only via
/// sub-ledgers, so they aren't hand-postable). Read seam only — never writes.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record GetChartOfAccountsQuery(int BookId, bool PostableOnly = false) : IRequest<IReadOnlyList<GlAccountModel>>;

public class GetChartOfAccountsHandler(AppDbContext db)
    : IRequestHandler<GetChartOfAccountsQuery, IReadOnlyList<GlAccountModel>>
{
    public async Task<IReadOnlyList<GlAccountModel>> Handle(GetChartOfAccountsQuery request, CancellationToken ct)
    {
        var q = db.GlAccounts
            .AsNoTracking()
            .Where(a => a.BookId == request.BookId && a.IsActive);

        if (request.PostableOnly)
            q = q.Where(a => a.IsPostable && !a.IsControlAccount);

        var rows = await q
            .OrderBy(a => a.AccountNumber)
            .Select(a => new
            {
                a.Id,
                a.AccountNumber,
                a.Name,
                a.AccountType,
                a.NormalBalance,
                a.IsPostable,
                a.IsControlAccount,
                a.RequiresJob,
                a.RequiresCostCenter,
            })
            .ToListAsync(ct);

        return rows
            .Select(a => new GlAccountModel(
                a.Id, a.AccountNumber, a.Name, a.AccountType.ToString(), a.NormalBalance.ToString(),
                a.IsPostable, a.IsControlAccount, a.RequiresJob, a.RequiresCostCenter))
            .ToList();
    }
}

/// <summary>One chart-of-accounts row for pickers/labels — enums as strings; flags the editor honours.</summary>
public record GlAccountModel(
    int Id,
    string AccountNumber,
    string Name,
    string AccountType,
    string NormalBalance,
    bool IsPostable,
    bool IsControlAccount,
    bool RequiresJob,
    bool RequiresCostCenter);
