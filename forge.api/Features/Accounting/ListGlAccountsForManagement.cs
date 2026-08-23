using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Chart-of-accounts rows for the management screen — unlike <see cref="GetChartOfAccountsQuery"/>
/// (the postable-account picker), this returns EVERY account including inactive ones (so they can be
/// reactivated) and computes <c>HasPostings</c> so the editor can lock the structural fields
/// (number / type / normal balance) once an account carries journal activity.
/// </summary>
public record ListGlAccountsForManagementQuery(int BookId) : IRequest<IReadOnlyList<GlAccountAdminModel>>;

public class ListGlAccountsForManagementHandler(AppDbContext db)
    : IRequestHandler<ListGlAccountsForManagementQuery, IReadOnlyList<GlAccountAdminModel>>
{
    public async Task<IReadOnlyList<GlAccountAdminModel>> Handle(ListGlAccountsForManagementQuery request, CancellationToken ct)
    {
        var postedAccountIds = await db.JournalLines.AsNoTracking()
            .Where(l => l.JournalEntry.BookId == request.BookId)
            .Select(l => l.GlAccountId)
            .Distinct()
            .ToListAsync(ct);
        var posted = postedAccountIds.ToHashSet();

        var rows = await db.GlAccounts.AsNoTracking()
            .Where(a => a.BookId == request.BookId)
            .OrderBy(a => a.AccountNumber)
            .Select(a => new
            {
                a.Id, a.AccountNumber, a.Name, a.AccountType, a.NormalBalance, a.ParentAccountId,
                a.IsPostable, a.IsControlAccount, a.IsActive, a.RequiresJob, a.RequiresCostCenter,
                a.CashFlowCategory, a.Description,
            })
            .ToListAsync(ct);

        return rows.Select(a => new GlAccountAdminModel(
            a.Id, a.AccountNumber, a.Name, a.AccountType.ToString(), a.NormalBalance.ToString(),
            a.ParentAccountId, a.IsPostable, a.IsControlAccount, a.IsActive, a.RequiresJob,
            a.RequiresCostCenter, a.CashFlowCategory?.ToString(), a.Description,
            HasPostings: posted.Contains(a.Id))).ToList();
    }
}
