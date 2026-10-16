using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// §7A conversion read: the book's active postable accounts — control accounts included, since opening
/// balances land on AR, AP and inventory too — for the opening-journal line picker. Reachable under
/// <c>CAP-ACCT-MIGRATION</c> because the regular chart-of-accounts read needs the GL view, which is
/// normally still off at cutover.
/// </summary>
[RequiresCapability("CAP-ACCT-MIGRATION")]
public record GetConversionAccountsQuery(int BookId) : IRequest<IReadOnlyList<GlAccountModel>>;

public class GetConversionAccountsHandler(AppDbContext db)
    : IRequestHandler<GetConversionAccountsQuery, IReadOnlyList<GlAccountModel>>
{
    public async Task<IReadOnlyList<GlAccountModel>> Handle(GetConversionAccountsQuery request, CancellationToken ct)
    {
        var rows = await db.GlAccounts
            .AsNoTracking()
            .Where(a => a.BookId == request.BookId && a.IsActive && a.IsPostable)
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
