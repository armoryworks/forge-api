using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
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

namespace Forge.Tests.Handlers.Accounting;

/// <summary>
/// Phase-0 manual-JE + trial-balance API handler tests (§5.5 / §5.9 acceptance:
/// "post a manual balanced journal" + "produce a trial balance"). These exercise
/// the MediatR command/query handlers end-to-end against the real
/// <see cref="ForgeGlPostingEngine"/> + <see cref="AccountDeterminationResolver"/>
/// + <see cref="TrialBalanceService"/> over an InMemory context (with an
/// in-process allocator, since InMemory can't run the row-lock SQL):
/// <list type="bullet">
///   <item>balanced post succeeds (entry posts, EntryNumber/period assigned);</item>
///   <item>unbalanced post throws <see cref="PostingException"/> (→ 400 via middleware);</item>
///   <item>the trial balance produced afterward balances (total Dr == total Cr).</item>
/// </list>
/// The capability gate itself is covered separately by
/// <c>CapabilityGateBehaviorTests</c>; the gate attribute on the command/query
/// keeps these handlers dark at runtime (CAP-ACCT-FULLGL OFF).
/// </summary>
public class AccountingGlHandlerTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int CashId = 100;     // Asset, postable, debit-normal
    private const int RevenueId = 101;  // Income, postable, credit-normal
    private const int OpenPeriodId = 1000;
    private const int PostingUserId = 42;

    /// <summary>In-process allocator (the InMemory provider can't run the row-lock SQL).</summary>
    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static IHttpContextAccessor HttpAccessorFor(int userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = principal });
        return accessor.Object;
    }

    private static IPostingEngine CreateEngine(AppDbContext db)
        => new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();

        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });

        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });

        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = FiscalYearId, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalYearStatus.Open,
        });

        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "Jan 2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31),
            Status = FiscalPeriodStatus.Open,
        });

        db.Set<GlAccount>().AddRange(
            new GlAccount
            {
                Id = CashId, BookId = BookId, AccountNumber = "1000", Name = "Cash",
                AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit,
                IsPostable = true, IsActive = true,
            },
            new GlAccount
            {
                Id = RevenueId, BookId = BookId, AccountNumber = "4000", Name = "Revenue",
                AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit,
                IsPostable = true, IsActive = true,
            });

        await db.SaveChangesAsync();
        return db;
    }

    private static CreateManualJournalEntryCommand BalancedCommand(decimal amount = 100m) => new(
        BookId: BookId,
        EntryDate: new DateOnly(2026, 1, 15),
        CurrencyId: UsdId,
        Memo: "Manual JE via API",
        AllowSoftClosedOverride: false,
        Lines:
        [
            new CreateManualJournalLineModel(CashId, null, null, null, null, null, amount, 0m, "Dr Cash"),
            new CreateManualJournalLineModel(RevenueId, null, null, null, null, null, 0m, amount, "Cr Revenue"),
        ]);

    [Fact]
    public async Task CreateManualJournalEntry_Balanced_PostsAndReturnsResult()
    {
        using var db = await SeedAsync();
        var handler = new CreateManualJournalEntryHandler(CreateEngine(db), HttpAccessorFor(PostingUserId));

        var result = await handler.Handle(BalancedCommand(250m), CancellationToken.None);

        result.Status.Should().Be(JournalEntryStatus.Posted.ToString());
        result.EntryNumber.Should().Be(1);
        result.BookId.Should().Be(BookId);
        result.FiscalPeriodId.Should().Be(OpenPeriodId);
        result.FiscalYearId.Should().Be(FiscalYearId);
        result.PostedBy.Should().Be(PostingUserId); // server-trusted principal recorded
        result.Lines.Should().HaveCount(2);
        result.Lines.Sum(l => l.Debit).Should().Be(250m);
        result.Lines.Sum(l => l.Credit).Should().Be(250m);

        // The entry actually persisted to the ledger.
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateManualJournalEntry_Unbalanced_ThrowsPostingException()
    {
        using var db = await SeedAsync();
        var handler = new CreateManualJournalEntryHandler(CreateEngine(db), HttpAccessorFor(PostingUserId));

        // Dr 100 / Cr 90 — the engine rejects it (→ 400 via the middleware mapping).
        var command = new CreateManualJournalEntryCommand(
            BookId: BookId,
            EntryDate: new DateOnly(2026, 1, 15),
            CurrencyId: UsdId,
            Memo: "Unbalanced",
            AllowSoftClosedOverride: false,
            Lines:
            [
                new CreateManualJournalLineModel(CashId, null, null, null, null, null, 100m, 0m, null),
                new CreateManualJournalLineModel(RevenueId, null, null, null, null, null, 0m, 90m, null),
            ]);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("UNBALANCED");
        // Nothing persisted.
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task GetTrialBalance_AfterBalancedPost_Balances()
    {
        using var db = await SeedAsync();
        var postHandler = new CreateManualJournalEntryHandler(CreateEngine(db), HttpAccessorFor(PostingUserId));
        var tbHandler = new GetTrialBalanceHandler(new TrialBalanceService(db));

        await postHandler.Handle(BalancedCommand(500m), CancellationToken.None);

        var tb = await tbHandler.Handle(new GetTrialBalanceQuery(BookId), CancellationToken.None);

        tb.IsBalanced.Should().BeTrue();
        tb.TotalDebit.Should().Be(500m);
        tb.TotalCredit.Should().Be(500m);
        tb.Rows.Sum(r => r.NetBalance).Should().Be(0m);
        tb.Rows.Should().Contain(r => r.GlAccountId == CashId && r.DebitTotal == 500m);
        tb.Rows.Should().Contain(r => r.GlAccountId == RevenueId && r.CreditTotal == 500m);
    }

    [Fact]
    public async Task GetTrialBalance_EmptyLedger_BalancesAtZero()
    {
        using var db = await SeedAsync();
        var tbHandler = new GetTrialBalanceHandler(new TrialBalanceService(db));

        var tb = await tbHandler.Handle(new GetTrialBalanceQuery(BookId), CancellationToken.None);

        tb.IsBalanced.Should().BeTrue();
        tb.TotalDebit.Should().Be(0m);
        tb.TotalCredit.Should().Be(0m);
        tb.Rows.Should().BeEmpty();
    }

    // ── Phase-1 STAGE E — P&L + Balance Sheet handlers (§6 Phase-1 row) ─────────

    [Fact]
    public async Task GetProfitAndLoss_AfterRevenuePost_ReturnsNetIncomeAndCaveat()
    {
        using var db = await SeedAsync();
        // Dr Cash / Cr Revenue 500 → revenue 500, no expense, net income 500.
        var postHandler = new CreateManualJournalEntryHandler(CreateEngine(db), HttpAccessorFor(PostingUserId));
        await postHandler.Handle(BalancedCommand(500m), CancellationToken.None);

        var pnlHandler = new GetProfitAndLossHandler(new FinancialStatementService(db, new SystemClock()));

        var pnl = await pnlHandler.Handle(
            new GetProfitAndLossQuery(BookId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            CancellationToken.None);

        pnl.TotalIncome.Should().Be(500m);
        pnl.TotalExpense.Should().Be(0m);
        pnl.NetIncome.Should().Be(500m);
        // Phase-1 incomplete-margin label (COGS not posted until Phase 2).
        pnl.CogsPosted.Should().BeFalse();
        pnl.MarginCaveat.Should().Contain("COGS");
    }

    [Fact]
    public async Task GetBalanceSheet_AfterRevenuePost_BalancesWithCurrentYearEarnings()
    {
        using var db = await SeedAsync();
        // Dr Cash 500 (asset) / Cr Revenue 500 (income) on 2026-01-15.
        var postHandler = new CreateManualJournalEntryHandler(CreateEngine(db), HttpAccessorFor(PostingUserId));
        await postHandler.Handle(BalancedCommand(500m), CancellationToken.None);

        var bsHandler = new GetBalanceSheetHandler(new FinancialStatementService(db, new SystemClock()));

        var bs = await bsHandler.Handle(
            new GetBalanceSheetQuery(BookId, new DateOnly(2026, 6, 30)),
            CancellationToken.None);

        // Asset (cash 500) balances against current-year earnings (revenue 500),
        // which is surfaced as equity before the Phase-3 year-end RE roll.
        bs.TotalAssets.Should().Be(500m);
        bs.TotalLiabilities.Should().Be(0m);
        bs.CurrentYearEarnings.Should().Be(500m);
        bs.TotalLiabilitiesAndEquity.Should().Be(500m);
        bs.IsBalanced.Should().BeTrue();
        bs.CogsPosted.Should().BeFalse();
    }
}
