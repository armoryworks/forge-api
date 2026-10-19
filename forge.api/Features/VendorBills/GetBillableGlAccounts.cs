using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorBills;

/// <summary>The accounts the bill dialog offers for a standalone line, ordered by account number.</summary>
public record GetBillableGlAccountsQuery : IRequest<List<BillableGlAccountModel>>;

public class GetBillableGlAccountsHandler(AppDbContext db)
    : IRequestHandler<GetBillableGlAccountsQuery, List<BillableGlAccountModel>>
{
    public async Task<List<BillableGlAccountModel>> Handle(GetBillableGlAccountsQuery request, CancellationToken cancellationToken)
    {
        var accounts = await BillableGlAccounts.Query(db)
            .OrderBy(a => a.AccountNumber)
            .ToListAsync(cancellationToken);
        return accounts
            .Select(a => new BillableGlAccountModel(a.Id, a.AccountNumber, a.Name, a.AccountType.ToString()))
            .ToList();
    }
}
