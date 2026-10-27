using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums.Accounting;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record GetDispositionExpenseAccountsQuery : IRequest<List<DispositionExpenseAccountResponseModel>>;

/// <summary>The active book's postable expense accounts, for the write-off choice on an Other disposition.</summary>
public class GetDispositionExpenseAccountsHandler(AppDbContext db)
    : IRequestHandler<GetDispositionExpenseAccountsQuery, List<DispositionExpenseAccountResponseModel>>
{
    public async Task<List<DispositionExpenseAccountResponseModel>> Handle(
        GetDispositionExpenseAccountsQuery request, CancellationToken cancellationToken)
    {
        var bookId = await db.Books.AsNoTracking()
            .Where(b => b.IsActive).OrderBy(b => b.Id)
            .Select(b => (int?)b.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (bookId is null)
            return [];

        return await db.GlAccounts.AsNoTracking()
            .Where(a => a.BookId == bookId && a.IsActive && a.IsPostable && a.AccountType == AccountType.Expense)
            .OrderBy(a => a.AccountNumber)
            .Select(a => new DispositionExpenseAccountResponseModel(a.Id, a.AccountNumber, a.Name))
            .ToListAsync(cancellationToken);
    }
}
