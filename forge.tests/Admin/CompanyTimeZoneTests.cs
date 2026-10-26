using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Data;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Admin;
using Forge.Api.Features.ShopFloor;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Admin;

/// <summary>
/// The company time zone (<c>company.time_zone</c>): validated against the server's zone database, saved from
/// the company profile, applied on request to books with no posted entry (locked books are counted and left
/// alone), used by the shop clock ahead of the book zone and by the seed for a new install's first book.
/// </summary>
public class CompanyTimeZoneTests
{
    private static AppDbContext CreateDb(params (int Id, string Zone)[] books)
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = 1, Code = "USD", Name = "US Dollar", Symbol = "$" });
        foreach (var (id, zone) in books)
            db.Set<Book>().Add(new Book
            {
                Id = id, Code = $"B{id}", Name = $"Book {id}", FunctionalCurrencyId = 1,
                ReportingTimeZone = zone, RoundingTolerance = 0.01m, IsActive = true,
            });
        db.SaveChanges();
        return db;
    }

    private static void AddPostedEntry(AppDbContext db, int bookId)
    {
        db.JournalEntries.Add(new JournalEntry
        {
            BookId = bookId, EntryNumber = 1, EntryDate = new DateOnly(2026, 1, 2),
            Status = JournalEntryStatus.Posted, Source = JournalSource.Manual, CurrencyId = 1,
        });
        db.SaveChanges();
    }

    private static Task<CompanyProfileResponseModel> SaveZoneAsync(AppDbContext db, string zone)
        => new UpdateCompanyProfileHandler(db)
            .Handle(new UpdateCompanyProfileCommand(null, null, null, null, null, zone), CancellationToken.None);

    [Theory]
    [InlineData("America/Denver", true)]
    [InlineData("Europe/Berlin", true)]
    [InlineData("", true)]
    [InlineData("Not/AZone", false)]
    public void Validator_AcceptsKnownZonesAndBlank(string zone, bool valid)
    {
        new UpdateCompanyProfileValidator()
            .Validate(new UpdateCompanyProfileCommand(null, null, null, null, null, zone))
            .IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task Get_WithoutACompanyZone_FallsBackToTheFirstBookZone()
    {
        using var db = CreateDb((1, "America/Chicago"));

        var profile = await new GetCompanyProfileHandler(db).Handle(new GetCompanyProfileQuery(), CancellationToken.None);

        profile.TimeZone.Should().Be("America/Chicago");
        profile.BooksAwaitingTimeZone.Should().Be(0);
    }

    [Fact]
    public async Task Save_StoresTheZone_AndCountsUnpostedBooksThatDiffer()
    {
        using var db = CreateDb((1, "America/New_York"), (2, "America/New_York"));
        AddPostedEntry(db, 2);

        var profile = await SaveZoneAsync(db, "America/Denver");

        profile.TimeZone.Should().Be("America/Denver");
        profile.BooksAwaitingTimeZone.Should().Be(1, "book 2 has posted entries and keeps its zone");
        (await db.SystemSettings.SingleAsync(s => s.Key == BookTimeZoneRules.CompanyTimeZoneKey)).Value
            .Should().Be("America/Denver");
    }

    [Fact]
    public async Task Apply_UpdatesUnpostedBooks_AndLeavesLockedBooksAlone()
    {
        using var db = CreateDb((1, "America/New_York"), (2, "America/New_York"));
        AddPostedEntry(db, 2);
        await SaveZoneAsync(db, "America/Denver");

        var result = await new ApplyCompanyTimeZoneToBooksHandler(db)
            .Handle(new ApplyCompanyTimeZoneToBooksCommand(), CancellationToken.None);

        result.Should().Be(new ApplyCompanyTimeZoneResultModel("America/Denver", 1, 1));
        (await db.Books.SingleAsync(b => b.Id == 1)).ReportingTimeZone.Should().Be("America/Denver");
        (await db.Books.SingleAsync(b => b.Id == 2)).ReportingTimeZone.Should().Be("America/New_York");
        (await db.ActivityLogs.SingleAsync(l => l.Action == "book-settings-updated")).EntityId.Should().Be(1);
    }

    [Fact]
    public async Task Apply_WithoutACompanyZone_IsRefused()
    {
        using var db = CreateDb((1, "America/New_York"));

        var act = () => new ApplyCompanyTimeZoneToBooksHandler(db)
            .Handle(new ApplyCompanyTimeZoneToBooksCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ShopClock_UsesTheCompanyZoneAheadOfTheBookZone()
    {
        using var db = CreateDb((1, "America/New_York"));
        (await ClockStateRules.ShopTimeZoneAsync(db)).Id.Should().Be("America/New_York");

        await SaveZoneAsync(db, "America/Denver");

        (await ClockStateRules.ShopTimeZoneAsync(db)).Id.Should().Be("America/Denver");
    }

    [Fact]
    public async Task Seed_NewInstall_TakesTheFirstBookZoneFromTheCompanySetting()
    {
        using var db = TestDbContextFactory.Create();
        db.SystemSettings.Add(new SystemSetting { Key = BookTimeZoneRules.CompanyTimeZoneKey, Value = "America/Phoenix" });
        await db.SaveChangesAsync();

        await SeedData.SeedAccountingAsync(db);

        (await db.Books.SingleAsync()).ReportingTimeZone.Should().Be("America/Phoenix");
    }

    [Fact]
    public async Task Seed_NewInstall_WithoutACompanyZone_KeepsTheFallback()
    {
        using var db = TestDbContextFactory.Create();

        await SeedData.SeedAccountingAsync(db);

        (await db.Books.SingleAsync()).ReportingTimeZone.Should().Be(BookTimeZoneRules.FallbackTimeZone);
    }
}
