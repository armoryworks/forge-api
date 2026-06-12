using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting.Qbo;

/// <summary>
/// QB-001 — the mapping-editor read: every postable, active GL account of the
/// book LEFT-joined with its QBO mapping (null QboAccountId = unmapped). One
/// query, no N+1.
/// </summary>
[RequiresCapability("CAP-ACCT-QBO-EXPORT")]
public record GetQboAccountMappingsQuery(int BookId) : IRequest<IReadOnlyList<QboAccountMappingModel>>;

public class GetQboAccountMappingsHandler(AppDbContext db)
    : IRequestHandler<GetQboAccountMappingsQuery, IReadOnlyList<QboAccountMappingModel>>
{
    public async Task<IReadOnlyList<QboAccountMappingModel>> Handle(
        GetQboAccountMappingsQuery request, CancellationToken cancellationToken)
    {
        return await (
            from account in db.GlAccounts.AsNoTracking()
            where account.BookId == request.BookId && account.IsPostable && account.IsActive
            join map in db.QboAccountMaps.AsNoTracking()
                on account.Id equals map.GlAccountId into maps
            from map in maps.DefaultIfEmpty()
            orderby account.AccountNumber
            select new QboAccountMappingModel(
                account.Id,
                account.AccountNumber,
                account.Name,
                map != null ? map.QboAccountId : null,
                map != null ? map.QboAccountName : null))
            .ToListAsync(cancellationToken);
    }
}
