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
/// Cross-cutting §12 GL policies: dimension-required (Job / CostCenter) on flagged accounts, and the
/// reversal-of-reversal policy (a reversal may itself be reversed, re-instating the original).
/// </summary>
public class Phase12GlPolicyTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int PeriodId = 1000;
    private const int CashId = 100;
    private const int WipId = 101;
    private const int DeptId = 102;
    private const int SalesId = 103;

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static readonly DateOnly Date = new(2026, 3, 1);

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear { Id = FiscalYearId, BookId = BookId, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod { Id = PeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = WipId, BookId = BookId, AccountNumber = "13200", Name = "WIP", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true, RequiresJob = true },
            new GlAccount { Id = DeptId, BookId = BookId, AccountNumber = "62000", Name = "Dept Expense", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true, RequiresCostCenter = true },
            new GlAccount { Id = SalesId, BookId = BookId, AccountNumber = "40000", Name = "Sales", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "WIP", GlAccountId = WipId },
            new AccountDeterminationRule { BookId = BookId, Key = "DEPT", GlAccountId = DeptId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = SalesId });
        await db.SaveChangesAsync();
        return db;
    }

    private static PostingRequest Entry(string tag, params PostingLine[] lines) => new()
    {
        BookId = BookId, EntryDate = Date, Source = JournalSource.Manual, CurrencyId = UsdId,
        IdempotencyKey = $"policy:{tag}", Lines = lines,
    };

    [Fact]
    public async Task RequiresJob_WithoutJob_Throws()
    {
        using var db = await SeedAsync();
        var act = async () => await Engine(db).PostAsync(Entry("nojob",
            new PostingLine { AccountKey = "WIP", Debit = 100m },
            new PostingLine { AccountKey = "CASH", Credit = 100m }), 7);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("JOB_REQUIRED");
    }

    [Fact]
    public async Task RequiresJob_WithJob_Succeeds()
    {
        using var db = await SeedAsync();
        var entry = await Engine(db).PostAsync(Entry("job",
            new PostingLine { AccountKey = "WIP", Debit = 100m, JobId = 1 },
            new PostingLine { AccountKey = "CASH", Credit = 100m }), 7);
        entry.Status.Should().Be(JournalEntryStatus.Posted);
    }

    [Fact]
    public async Task RequiresCostCenter_WithoutCostCenter_Throws()
    {
        using var db = await SeedAsync();
        var act = async () => await Engine(db).PostAsync(Entry("nocc",
            new PostingLine { AccountKey = "DEPT", Debit = 100m },
            new PostingLine { AccountKey = "CASH", Credit = 100m }), 7);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("COST_CENTER_REQUIRED");
    }

    [Fact]
    public async Task ReversalOfReversal_Succeeds_AndReinstatesOriginal()
    {
        using var db = await SeedAsync();
        var engine = Engine(db);
        var original = await engine.PostAsync(Entry("orig",
            new PostingLine { AccountKey = "CASH", Debit = 100m },
            new PostingLine { AccountKey = "SALES_REVENUE", Credit = 100m }), 7);

        var reversal = await engine.ReverseAsync(original.Id, Date, "correct", 7);
        // The reversal is itself Posted with no ReversedByEntryId → it may be reversed in turn (§12 policy).
        var reReversal = await engine.ReverseAsync(reversal.Id, Date, "re-correct", 7);

        reReversal.Status.Should().Be(JournalEntryStatus.Posted);
        (await db.JournalEntries.IgnoreQueryFilters().SingleAsync(e => e.Id == reversal.Id))
            .Status.Should().Be(JournalEntryStatus.Reversed);

        // Net Cash across original (+100) + reversal (−100) + re-reversal (+100) = +100 (original re-instated).
        var cashNet = await db.JournalLines.IgnoreQueryFilters()
            .Where(l => l.GlAccountId == CashId)
            .SumAsync(l => l.Debit - l.Credit);
        cashNet.Should().Be(100m);
    }
}
