using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Phase-3 — the pre-close checklist for a fiscal period (evaluated as of the period end). Lets the UI show
/// what's clean/dirty before a hard-close. CAP-ACCT-FULLGL gated.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record GetCloseChecklistQuery(int PeriodId) : IRequest<CloseChecklistResult>;

public class GetCloseChecklistHandler(IPeriodCloseChecklistService checklist, AppDbContext db)
    : IRequestHandler<GetCloseChecklistQuery, CloseChecklistResult>
{
    public async Task<CloseChecklistResult> Handle(GetCloseChecklistQuery request, CancellationToken ct)
    {
        var period = await db.FiscalPeriods
            .AsNoTracking()
            .Where(p => p.Id == request.PeriodId)
            .Select(p => new { p.FiscalYear.BookId, p.EndDate })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Fiscal period {request.PeriodId} not found.");

        return await checklist.EvaluateAsync(period.BookId, period.EndDate, ct);
    }
}
