using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Phase-3 — the book's fiscal calendar (years + their periods, with statuses) for the close screen.
/// Read-only; <c>CAP-ACCT-FULLGL</c> gated.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record GetFiscalCalendarQuery(int BookId) : IRequest<IReadOnlyList<FiscalYearModel>>;

public class GetFiscalCalendarHandler(AppDbContext db)
    : IRequestHandler<GetFiscalCalendarQuery, IReadOnlyList<FiscalYearModel>>
{
    public async Task<IReadOnlyList<FiscalYearModel>> Handle(GetFiscalCalendarQuery request, CancellationToken ct)
    {
        var years = await db.FiscalYears
            .AsNoTracking()
            .Where(y => y.BookId == request.BookId)
            .Include(y => y.Periods)
            .OrderByDescending(y => y.StartDate)
            .ToListAsync(ct);

        return years
            .Select(y => new FiscalYearModel(
                y.Id, y.Name, y.StartDate, y.EndDate, y.Status,
                y.Periods
                    .OrderBy(p => p.PeriodNumber)
                    .Select(p => new FiscalPeriodModel(
                        p.Id, p.FiscalYearId, p.PeriodNumber, p.Name, p.StartDate, p.EndDate, p.Status))
                    .ToList()))
            .ToList();
    }
}
