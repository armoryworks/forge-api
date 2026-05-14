using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Expenses;

public record GetUpcomingExpensesQuery(int DaysAhead = 90, string? Classification = null) : IRequest<List<UpcomingExpenseResponseModel>>;

public class GetUpcomingExpensesHandler(AppDbContext db) : IRequestHandler<GetUpcomingExpensesQuery, List<UpcomingExpenseResponseModel>>
{
    public async Task<List<UpcomingExpenseResponseModel>> Handle(GetUpcomingExpensesQuery request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var horizon = now.AddDays(request.DaysAhead);

        var recurring = await db.RecurringExpenses
            .AsNoTracking()
            .Where(r => r.IsActive && r.NextOccurrenceDate <= horizon)
            .Where(r => r.EndDate == null || r.EndDate > now)
            .OrderBy(r => r.NextOccurrenceDate)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.Classification))
            recurring = recurring.Where(r => r.Classification == request.Classification).ToList();

        var result = new List<UpcomingExpenseResponseModel>();

        foreach (var r in recurring)
        {
            var date = r.NextOccurrenceDate;
            while (date <= horizon)
            {
                if (r.EndDate.HasValue && date > r.EndDate.Value) break;

                result.Add(new UpcomingExpenseResponseModel(
                    r.Id,
                    r.Description,
                    r.Category,
                    r.Classification,
                    r.Vendor,
                    r.Amount,
                    date,
                    r.Frequency,
                    r.AutoApprove
                ));

                date = AdvanceDate(date, r.Frequency);
            }
        }

        return result.OrderBy(e => e.DueDate).ToList();
    }

    private static DateTimeOffset AdvanceDate(DateTimeOffset date, RecurrenceFrequency frequency) => frequency switch
    {
        RecurrenceFrequency.Weekly => date.AddDays(7),
        RecurrenceFrequency.Biweekly => date.AddDays(14),
        RecurrenceFrequency.Monthly => date.AddMonths(1),
        RecurrenceFrequency.Quarterly => date.AddMonths(3),
        RecurrenceFrequency.Annually => date.AddYears(1),
        _ => date.AddMonths(1),
    };
}
