using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Where a system posting dated in a closed period lands: the date itself while its period is Open, otherwise the
/// first day of the first Open period after it. A soft- or hard-closed period is never reopened or posted into. When
/// no Open period follows, the date is returned unchanged and the posting engine reports the closed period.
/// </summary>
public static class OpenPeriodDate
{
    /// <summary>The first Open date on or after <paramref name="date"/> in the book's fiscal calendar.</summary>
    public static async Task<DateOnly> FirstOpenOnOrAfterAsync(
        AppDbContext db, int bookId, DateOnly date, CancellationToken ct = default)
    {
        var periods = await db.FiscalPeriods.AsNoTracking()
            .Where(p => p.FiscalYear.BookId == bookId && p.EndDate >= date)
            .Select(p => new { p.StartDate, p.EndDate, p.Status, p.PeriodNumber })
            .ToListAsync(ct);

        bool IsOpenOn(DateOnly day)
        {
            var covering = periods.Where(p => p.StartDate <= day && p.EndDate >= day).ToList();
            return covering.Count > 0 && covering.All(p => p.Status == FiscalPeriodStatus.Open);
        }

        if (IsOpenOn(date))
            return date;

        foreach (var period in periods
                     .Where(p => p.Status == FiscalPeriodStatus.Open && p.StartDate > date)
                     .OrderBy(p => p.StartDate).ThenBy(p => p.PeriodNumber))
        {
            if (IsOpenOn(period.StartDate))
                return period.StartDate;
        }

        return date;
    }
}
