using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Serilog;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Rolls a book's fiscal calendar forward. The seed creates only the year it was installed in, and posting
/// needs a period covering every entry date, so each book gets its following year (twelve monthly periods,
/// all Open) before the current one runs out: on boot, daily, and when a year is closed. Years are only ever
/// added after the book's latest year, never in front of or between existing ones. The training sandbox
/// book keeps its fixed calendar.
/// </summary>
public static class FiscalCalendar
{
    private const int MaxYearsPerRun = 10;

    /// <summary>
    /// For every active book that has a fiscal calendar, adds following years until one covers
    /// <paramref name="through"/>. Returns the number of years added.
    /// </summary>
    public static async Task<int> EnsureCoverageAsync(AppDbContext db, DateOnly through, CancellationToken ct = default)
    {
        var bookIds = await db.Books.AsNoTracking()
            .Where(b => b.IsActive && b.Code != Training.TrainingSandboxService.BookCode)
            .Select(b => b.Id)
            .ToListAsync(ct);

        var added = 0;
        foreach (var bookId in bookIds)
        {
            for (var i = 0; i < MaxYearsPerRun; i++)
            {
                var latest = await db.FiscalYears
                    .Where(y => y.BookId == bookId)
                    .OrderByDescending(y => y.EndDate)
                    .FirstOrDefaultAsync(ct);
                if (latest is null || latest.EndDate >= through)
                    break;

                if (await AddFollowingYearAsync(db, latest, ct) is null)
                    break;
                added++;
            }
        }

        return added;
    }

    /// <summary>
    /// Returns the year that starts the day after <paramref name="year"/> ends, creating it when the book has
    /// no year covering that day. Returns null only when a year of the same name already exists elsewhere in
    /// the calendar, which is logged.
    /// </summary>
    public static async Task<FiscalYear?> EnsureFollowingYearAsync(AppDbContext db, FiscalYear year, CancellationToken ct = default)
    {
        var start = year.EndDate.AddDays(1);
        var existing = await db.FiscalYears
            .FirstOrDefaultAsync(y => y.BookId == year.BookId && y.StartDate <= start && y.EndDate >= start, ct);
        return existing ?? await AddFollowingYearAsync(db, year, ct);
    }

    private static async Task<FiscalYear?> AddFollowingYearAsync(AppDbContext db, FiscalYear year, CancellationToken ct)
    {
        var start = year.EndDate.AddDays(1);
        var end = start.AddYears(1).AddDays(-1);
        var name = start.Month == 1 && start.Day == 1
            ? start.Year.ToString(CultureInfo.InvariantCulture)
            : $"FY{end.Year.ToString(CultureInfo.InvariantCulture)}";

        if (await db.FiscalYears.AnyAsync(y => y.BookId == year.BookId && y.Name == name, ct))
        {
            Log.Warning(
                "Fiscal year {Name} already exists for book {BookId} but does not start on {Start}; the calendar was not extended.",
                name, year.BookId, start);
            return null;
        }

        var next = new FiscalYear
        {
            BookId = year.BookId,
            Name = name,
            StartDate = start,
            EndDate = end,
            Status = FiscalYearStatus.Open,
        };
        for (var month = 1; month <= 12; month++)
        {
            var periodStart = start.AddMonths(month - 1);
            next.Periods.Add(new FiscalPeriod
            {
                PeriodNumber = month,
                Name = periodStart.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                StartDate = periodStart,
                EndDate = month == 12 ? end : start.AddMonths(month).AddDays(-1),
                Status = FiscalPeriodStatus.Open,
            });
        }

        db.FiscalYears.Add(next);
        await db.SaveChangesAsync(ct);

        Log.Information(
            "Added fiscal year {Name} ({Start} to {End}) with 12 periods for book {BookId}.",
            name, start, end, year.BookId);
        return next;
    }
}
