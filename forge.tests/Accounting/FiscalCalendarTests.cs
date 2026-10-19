using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Accounting.Training;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// The fiscal calendar rolls forward on its own: a book whose only year is the one it was installed in gets
/// the following year (twelve monthly Open periods) before the current one runs out, so postings dated after
/// the year end find a period instead of failing with PERIOD_NOT_FOUND.
/// </summary>
public class FiscalCalendarTests
{
    private static async Task<(AppDbContext Db, Book Book)> SeedAsync(string code = "MAIN")
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = 1, Code = "USD", Name = "US Dollar", Symbol = "$" });
        var book = new Book
        {
            Code = code, Name = code, FunctionalCurrencyId = 1,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        db.FiscalYears.Add(new FiscalYear
        {
            BookId = book.Id, Name = "2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalYearStatus.Open,
        });
        await db.SaveChangesAsync();
        return (db, book);
    }

    [Fact]
    public async Task Coverage_WithinTheYear_AddsNothing()
    {
        var (db, _) = await SeedAsync();
        using var _db = db;

        (await FiscalCalendar.EnsureCoverageAsync(db, new DateOnly(2026, 11, 19))).Should().Be(0);
        (await db.FiscalYears.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Coverage_PastTheYearEnd_AddsTheFollowingYearWithTwelveMonthlyPeriods()
    {
        var (db, book) = await SeedAsync();
        using var _db = db;

        (await FiscalCalendar.EnsureCoverageAsync(db, new DateOnly(2027, 1, 15))).Should().Be(1);

        var next = await db.FiscalYears.Include(y => y.Periods).SingleAsync(y => y.Name == "2027");
        next.BookId.Should().Be(book.Id);
        next.StartDate.Should().Be(new DateOnly(2027, 1, 1));
        next.EndDate.Should().Be(new DateOnly(2027, 12, 31));
        var periods = next.Periods.OrderBy(p => p.PeriodNumber).ToList();
        periods.Should().HaveCount(12);
        periods[0].StartDate.Should().Be(new DateOnly(2027, 1, 1));
        periods[1].EndDate.Should().Be(new DateOnly(2027, 2, 28));
        periods[11].Name.Should().Be("Dec 2027");
        periods[11].EndDate.Should().Be(new DateOnly(2027, 12, 31));
        periods.Should().OnlyContain(p => p.Status == FiscalPeriodStatus.Open);

        (await FiscalCalendar.EnsureCoverageAsync(db, new DateOnly(2027, 1, 15))).Should().Be(0, "a re-run is a no-op");
    }

    [Fact]
    public async Task Coverage_CatchesUpSeveralYears()
    {
        var (db, _) = await SeedAsync();
        using var _db = db;

        (await FiscalCalendar.EnsureCoverageAsync(db, new DateOnly(2029, 3, 1))).Should().Be(3);
        (await db.FiscalYears.Select(y => y.Name).ToListAsync()).Should().BeEquivalentTo(["2026", "2027", "2028", "2029"]);
    }

    [Fact]
    public async Task Coverage_LeavesTheTrainingSandboxCalendarAlone()
    {
        var (db, _) = await SeedAsync(TrainingSandboxService.BookCode);
        using var _db = db;

        (await FiscalCalendar.EnsureCoverageAsync(db, new DateOnly(2027, 6, 1))).Should().Be(0);
    }

    [Fact]
    public async Task NonCalendarFiscalYear_RollsToTheNextTwelveMonths()
    {
        var (db, book) = await SeedAsync();
        using var _db = db;
        var july = new FiscalYear
        {
            BookId = book.Id, Name = "FY2027", StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2027, 6, 30),
        };

        var next = await FiscalCalendar.EnsureFollowingYearAsync(db, july);

        next!.Name.Should().Be("FY2028");
        next.StartDate.Should().Be(new DateOnly(2027, 7, 1));
        next.EndDate.Should().Be(new DateOnly(2028, 6, 30));
    }
}
