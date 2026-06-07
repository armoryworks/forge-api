using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <inheritdoc />
public sealed class PostingDateResolver(AppDbContext db) : IPostingDateResolver
{
    public async Task<DateOnly> ResolveOpenPostingDateAsync(
        int bookId, DateOnly desiredDate, CancellationToken ct = default)
    {
        var covering = await db.FiscalPeriods
            .Include(p => p.FiscalYear)
            .Where(p => p.FiscalYear.BookId == bookId && p.StartDate <= desiredDate && p.EndDate >= desiredDate)
            .FirstOrDefaultAsync(ct);

        // The desired date is fine when its own period is open.
        if (covering is { Status: FiscalPeriodStatus.Open })
            return desiredDate;

        // Otherwise catch up into the next open period on/after the desired date.
        var nextOpen = await db.FiscalPeriods
            .Include(p => p.FiscalYear)
            .Where(p => p.FiscalYear.BookId == bookId
                && p.Status == FiscalPeriodStatus.Open
                && p.EndDate >= desiredDate)
            .OrderBy(p => p.StartDate)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException(
                $"No open fiscal period on/after {desiredDate} for book {bookId} to post into.");

        return nextOpen.StartDate > desiredDate ? nextOpen.StartDate : desiredDate;
    }
}
