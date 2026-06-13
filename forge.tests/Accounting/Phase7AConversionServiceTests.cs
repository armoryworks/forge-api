using FluentAssertions;
using Microsoft.EntityFrameworkCore;

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
/// §7A conversion — opening-balance journal. Proves a balanced opening journal (incl. an AR open item with a
/// party) posts as Source=Conversion, is idempotent per book, rejects an unbalanced load, and enforces the
/// control-line party requirement on AR/AP open items.
/// </summary>
public class Phase7AConversionServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int PeriodId = 1000;
    private const int CashId = 100;
    private const int ArId = 110;
    private const int ObeId = 300;

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static ConversionService Service(AppDbContext db) => new(db, Engine(db));

    private static readonly DateOnly AsOf = new(2026, 1, 1);

    private static async Task<(AppDbContext db, int customerId)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear { Id = FiscalYearId, BookId = BookId, Name = "FY2026", StartDate = AsOf, EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod { Id = PeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026", StartDate = AsOf, EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ArId, BookId = BookId, AccountNumber = "11000", Name = "Accounts Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true },
            new GlAccount { Id = ObeId, BookId = BookId, AccountNumber = "39000", Name = "Opening Balance Equity", AccountType = AccountType.Equity, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = ArId },
            new AccountDeterminationRule { BookId = BookId, Key = "OPENING_BALANCE_EQUITY", GlAccountId = ObeId });
        var customer = new Customer { CompanyName = "Acme", IsActive = true };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();
        return (db, customer.Id);
    }

    private static PostOpeningBalancesModel BalancedOpening(int customerId) => new(BookId, AsOf,
        [
            new OpeningBalanceLineModel("CASH", null, 1000m, 0m, null, null, "Opening cash"),
            new OpeningBalanceLineModel("AR_CONTROL", null, 500m, 0m, SubledgerPartyType.Customer, customerId, "Open invoice"),
            new OpeningBalanceLineModel("OPENING_BALANCE_EQUITY", null, 0m, 1500m, null, null, "Opening equity"),
        ]);

    [Fact]
    public async Task PostsBalancedOpeningJournal_AsConversion()
    {
        var (db, customerId) = await SeedAsync();

        var result = await Service(db).PostOpeningBalancesAsync(BalancedOpening(customerId), postedByUserId: 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .SingleAsync(e => e.Id == result.JournalEntryId);
        entry.Source.Should().Be(JournalSource.Conversion);
        entry.SourceType.Should().Be("OpeningBalance");
        entry.Lines.Single(l => l.GlAccountId == ArId).SubledgerPartyId.Should().Be(customerId);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
        result.TotalDebit.Should().Be(1500m);
    }

    [Fact]
    public async Task Idempotent_PerBook()
    {
        var (db, customerId) = await SeedAsync();
        var svc = Service(db);

        var first = await svc.PostOpeningBalancesAsync(BalancedOpening(customerId), 7);
        var second = await svc.PostOpeningBalancesAsync(BalancedOpening(customerId), 7);

        second.JournalEntryId.Should().Be(first.JournalEntryId);
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Unbalanced_Throws()
    {
        var (db, _) = await SeedAsync();
        var model = new PostOpeningBalancesModel(BookId, AsOf,
            [
                new OpeningBalanceLineModel("CASH", null, 1000m, 0m, null, null, "Opening cash"),
                new OpeningBalanceLineModel("OPENING_BALANCE_EQUITY", null, 0m, 900m, null, null, "Opening equity"),
            ]);

        var act = async () => await Service(db).PostOpeningBalancesAsync(model, 7);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("UNBALANCED");
    }

    [Fact]
    public async Task ArOpenItem_WithoutParty_Throws()
    {
        var (db, _) = await SeedAsync();
        var model = new PostOpeningBalancesModel(BookId, AsOf,
            [
                new OpeningBalanceLineModel("AR_CONTROL", null, 500m, 0m, null, null, "AR with no party"),
                new OpeningBalanceLineModel("OPENING_BALANCE_EQUITY", null, 0m, 500m, null, null, "Opening equity"),
            ]);

        var act = async () => await Service(db).PostOpeningBalancesAsync(model, 7);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("CONTROL_LINE_PARTY_REQUIRED");
    }
}
