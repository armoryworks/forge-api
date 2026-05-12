using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Expenses;

public record GetRecurringExpensesQuery(string? Classification) : IRequest<List<RecurringExpenseResponseModel>>;

public class GetRecurringExpensesHandler(AppDbContext db) : IRequestHandler<GetRecurringExpensesQuery, List<RecurringExpenseResponseModel>>
{
    public async Task<List<RecurringExpenseResponseModel>> Handle(GetRecurringExpensesQuery request, CancellationToken cancellationToken)
    {
        var query = db.RecurringExpenses.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Classification))
            query = query.Where(r => r.Classification == request.Classification);

        var users = await db.Users
            .AsNoTracking()
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken);

        var items = await query
            .OrderBy(r => r.NextOccurrenceDate)
            .ToListAsync(cancellationToken);

        return items.Select(r => new RecurringExpenseResponseModel(
            r.Id,
            r.UserId,
            users.GetValueOrDefault(r.UserId, "Unknown"),
            r.Amount,
            r.Category,
            r.Classification,
            r.Description,
            r.Vendor,
            r.Frequency,
            r.NextOccurrenceDate,
            r.LastGeneratedDate,
            r.EndDate,
            r.IsActive,
            r.AutoApprove,
            r.CreatedAt
        )).ToList();
    }
}
