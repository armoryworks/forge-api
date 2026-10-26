using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Journal template editing: update replaces the header and lines, line rules are enforced on create and
/// update, an inactive template refuses posting, delete is a soft delete that leaves posted entries alone,
/// and the counterparty lookup returns active customers or vendors by name.
/// </summary>
public class JournalTemplateEditTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int CashId = 100;
    private const int RentId = 610;
    private const int UtilitiesId = 620;

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static JournalTemplateService Service(AppDbContext db)
        => new(db, new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock()));

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear { Id = 10, BookId = BookId, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod { Id = 1000, FiscalYearId = 10, PeriodNumber = 1, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = RentId, BookId = BookId, AccountNumber = "61000", Name = "Rent Expense", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = UtilitiesId, BookId = BookId, AccountNumber = "62000", Name = "Utilities", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().Add(new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId });
        await db.SaveChangesAsync();
        return db;
    }

    private static CreateJournalTemplateModel Rent() => new(BookId, "Monthly rent", "Office lease", "Rent", false,
    [
        new JournalTemplateLineModel(null, RentId, 1200m, 0m, "Rent expense", null, null),
        new JournalTemplateLineModel("CASH", null, 0m, 1200m, "Cash", null, null),
    ]);

    private static UpdateJournalTemplateModel RentAndUtilities(bool isActive = true) => new(
        "Monthly occupancy", null, "Occupancy", true, isActive,
    [
        new JournalTemplateLineModel(null, RentId, 1200m, 0m, "Rent expense", null, null),
        new JournalTemplateLineModel(null, UtilitiesId, 300m, 0m, "Utilities", null, null),
        new JournalTemplateLineModel("CASH", null, 0m, 1500m, "Cash", null, null),
    ]);

    [Fact]
    public async Task Update_replaces_the_header_and_lines()
    {
        using var db = await SeedAsync();
        var created = await Service(db).CreateAsync(Rent());

        var updated = await Service(db).UpdateAsync(created.Id, RentAndUtilities());

        updated.Name.Should().Be("Monthly occupancy");
        updated.Memo.Should().Be("Occupancy");
        updated.AutoReverseNextPeriod.Should().BeTrue();
        updated.Lines.Select(l => l.GlAccountId).Should().Equal(RentId, UtilitiesId, null);
        (await db.JournalTemplateLines.CountAsync(l => l.JournalTemplateId == created.Id)).Should().Be(3);
        (await Service(db).GetAsync(created.Id))!.Lines.Sum(l => l.Credit).Should().Be(1500m);
    }

    [Fact]
    public async Task Posting_uses_the_updated_lines()
    {
        using var db = await SeedAsync();
        var created = await Service(db).CreateAsync(Rent());
        await Service(db).UpdateAsync(created.Id, RentAndUtilities());

        var posted = await Service(db).PostFromTemplateAsync(created.Id, new DateOnly(2026, 2, 1), null, 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync(e => e.Id == posted.JournalEntryId);
        entry.Lines.Should().HaveCount(3);
        entry.Lines.Single(l => l.GlAccountId == UtilitiesId).Debit.Should().Be(300m);
    }

    [Fact]
    public async Task An_inactive_template_refuses_posting()
    {
        using var db = await SeedAsync();
        var created = await Service(db).CreateAsync(Rent());
        await Service(db).UpdateAsync(created.Id, RentAndUtilities(isActive: false));

        var act = () => Service(db).PostFromTemplateAsync(created.Id, new DateOnly(2026, 2, 1), null, 7);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*inactive*");
    }

    [Theory]
    [InlineData("CASH", RentId, 10, 0, "*exactly one of an account*")]
    [InlineData(null, null, 10, 0, "*exactly one of an account*")]
    [InlineData(null, RentId, -5, 0, "*cannot be negative*")]
    [InlineData(null, RentId, 10, 10, "*either a debit or a credit*")]
    public async Task Update_refuses_a_malformed_line(string? key, int? accountId, int debit, int credit, string message)
    {
        using var db = await SeedAsync();
        var created = await Service(db).CreateAsync(Rent());
        var model = RentAndUtilities() with
        {
            Lines = [new JournalTemplateLineModel(key, accountId, debit, credit, null, null, null)],
        };

        var act = () => Service(db).UpdateAsync(created.Id, model);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
    }

    [Fact]
    public async Task Create_refuses_a_party_without_its_type()
    {
        using var db = await SeedAsync();
        var model = Rent() with
        {
            Lines = [new JournalTemplateLineModel(null, RentId, 10m, 0m, null, null, 5)],
        };

        var act = () => Service(db).CreateAsync(model);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*both its type and the party*");
    }

    [Fact]
    public async Task Update_of_an_unknown_template_is_not_found()
    {
        using var db = await SeedAsync();

        var act = () => Service(db).UpdateAsync(404, RentAndUtilities());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Delete_soft_deletes_the_template_and_keeps_its_posted_entries()
    {
        using var db = await SeedAsync();
        var created = await Service(db).CreateAsync(Rent());
        var posted = await Service(db).PostFromTemplateAsync(created.Id, new DateOnly(2026, 1, 1), null, 7);
        var now = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(now);

        await new DeleteJournalTemplateHandler(db, clock.Object).Handle(new DeleteJournalTemplateCommand(created.Id), default);

        (await Service(db).ListAsync(BookId)).Should().BeEmpty();
        (await db.JournalTemplates.IgnoreQueryFilters().SingleAsync(t => t.Id == created.Id)).DeletedAt.Should().Be(now);
        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync(e => e.Id == posted.JournalEntryId)).Should().BeTrue();
    }

    [Fact]
    public async Task Get_of_a_deleted_template_is_not_found()
    {
        using var db = await SeedAsync();
        var created = await Service(db).CreateAsync(Rent());
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.UnixEpoch);
        await new DeleteJournalTemplateHandler(db, clock.Object).Handle(new DeleteJournalTemplateCommand(created.Id), default);

        var act = () => new GetJournalTemplateHandler(Service(db)).Handle(new GetJournalTemplateQuery(created.Id), default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Party_lookup_returns_active_customers_or_vendors_matching_the_search()
    {
        using var db = await SeedAsync();
        db.Customers.AddRange(
            new Customer { Name = "Acme Plastics", IsActive = true },
            new Customer { Name = "Bolt Works", CompanyName = "Acme Holdings", IsActive = true },
            new Customer { Name = "Acme Old", IsActive = false });
        db.Vendors.AddRange(
            new Vendor { CompanyName = "Acme Resin Supply", IsActive = true },
            new Vendor { CompanyName = "Steel Depot", IsActive = true });
        await db.SaveChangesAsync();
        var handler = new SearchJournalPartiesHandler(db);

        var customers = await handler.Handle(new SearchJournalPartiesQuery(SubledgerPartyType.Customer, "acme"), default);
        var vendors = await handler.Handle(new SearchJournalPartiesQuery(SubledgerPartyType.Vendor, "ACME"), default);

        customers.Select(c => c.Name).Should().Equal("Acme Plastics", "Bolt Works");
        customers.Should().OnlyContain(c => c.PartyType == SubledgerPartyType.Customer);
        vendors.Select(v => v.Name).Should().Equal("Acme Resin Supply");
    }

    [Fact]
    public async Task Party_lookup_by_id_returns_that_party_even_when_inactive()
    {
        using var db = await SeedAsync();
        var old = new Customer { Name = "Acme Old", IsActive = false };
        db.Customers.Add(old);
        await db.SaveChangesAsync();

        var result = await new SearchJournalPartiesHandler(db)
            .Handle(new SearchJournalPartiesQuery(SubledgerPartyType.Customer, null, old.Id), default);

        result.Should().ContainSingle().Which.Name.Should().Be("Acme Old");
    }
}
