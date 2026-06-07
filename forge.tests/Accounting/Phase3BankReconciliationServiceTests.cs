using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Phase-3 — bank reconciliation. Proves the worksheet snapshots the cash lines, the clearing math
/// reconciles (statement ending balance + outstanding net == GL cash balance), finalize requires balance and
/// is blocked otherwise, a finalized rec is immutable, and its cleared lines are excluded from the next rec.
/// </summary>
public class Phase3BankReconciliationServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int PeriodId = 1000;
    private const int CashId = 100;
    private const int SalesId = 101;
    private const int ExpenseId = 102;

    private static readonly DateOnly StatementDate = new(2026, 6, 30);

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static BankReconciliationService Service(AppDbContext db) => new(db, new SystemClock());

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
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = PeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open,
        });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = SalesId, BookId = BookId, AccountNumber = "40000", Name = "Sales", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ExpenseId, BookId = BookId, AccountNumber = "60000", Name = "G&A", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = SalesId },
            new AccountDeterminationRule { BookId = BookId, Key = "OPERATING_EXPENSE", GlAccountId = ExpenseId });
        await db.SaveChangesAsync();
        return db;
    }

    private static int _seq;
    private static Task DepositAsync(AppDbContext db, decimal amount, DateOnly? on = null)
        => PostAsync(db, "CASH", "SALES_REVENUE", amount, on);
    private static Task WithdrawalAsync(AppDbContext db, decimal amount, DateOnly? on = null)
        => PostAsync(db, "OPERATING_EXPENSE", "CASH", amount, on);

    private static Task PostAsync(AppDbContext db, string drKey, string crKey, decimal amount, DateOnly? on)
        => Engine(db).PostAsync(new Forge.Core.Models.Accounting.PostingRequest
        {
            BookId = BookId, EntryDate = on ?? new DateOnly(2026, 6, 1), Source = JournalSource.Manual, CurrencyId = UsdId,
            IdempotencyKey = $"br:{_seq++}",
            Lines =
            [
                new Forge.Core.Models.Accounting.PostingLine { AccountKey = drKey, Debit = amount, Description = "dr" },
                new Forge.Core.Models.Accounting.PostingLine { AccountKey = crKey, Credit = amount, Description = "cr" },
            ],
        }, 7);

    private static async Task<(long depositLineId, long withdrawalLineId)> CashLineIdsAsync(
        AppDbContext db, decimal deposit, decimal withdrawal)
    {
        var cashLines = await db.JournalLines.Where(l => l.GlAccountId == CashId).ToListAsync();
        return (cashLines.Single(l => l.Debit == deposit).Id, cashLines.Single(l => l.Credit == withdrawal).Id);
    }

    [Fact]
    public async Task Start_SnapshotsCashLines_AndBookBalance()
    {
        using var db = await SeedAsync();
        await DepositAsync(db, 1000m);
        await WithdrawalAsync(db, 300m);

        var ws = await Service(db).StartAsync(BookId, CashId, StatementDate, statementEndingBalance: 1000m);

        ws.Items.Should().HaveCount(2);
        ws.BookBalance.Should().Be(700m);
        ws.Status.Should().Be(BankReconciliationStatus.Draft);
    }

    [Fact]
    public async Task ClearDeposit_OutstandingCheck_Reconciles()
    {
        using var db = await SeedAsync();
        await DepositAsync(db, 1000m);
        await WithdrawalAsync(db, 300m);
        var (depositLineId, _) = await CashLineIdsAsync(db, 1000m, 300m);
        var svc = Service(db);

        // Bank cleared the deposit (1000) but not the 300 check → statement = 1000.
        var ws = await svc.StartAsync(BookId, CashId, StatementDate, statementEndingBalance: 1000m);
        ws.IsReconciled.Should().BeFalse(); // nothing cleared yet

        ws = await svc.SetClearedAsync(ws.ReconciliationId, depositLineId, true);

        ws.OutstandingTotal.Should().Be(-300m);   // the outstanding check
        ws.Difference.Should().Be(0m);
        ws.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Finalize_WhenReconciled_LocksRec()
    {
        using var db = await SeedAsync();
        await DepositAsync(db, 1000m);
        var depositLineId = (await db.JournalLines.Where(l => l.GlAccountId == CashId).SingleAsync(l => l.Debit == 1000m)).Id;
        var svc = Service(db);
        var ws = await svc.StartAsync(BookId, CashId, StatementDate, statementEndingBalance: 1000m);
        ws = await svc.SetClearedAsync(ws.ReconciliationId, depositLineId, true);
        ws.IsReconciled.Should().BeTrue();

        var finalized = await svc.FinalizeAsync(ws.ReconciliationId);
        finalized.Status.Should().Be(BankReconciliationStatus.Finalized);
    }

    [Fact]
    public async Task Finalize_OutOfBalance_Throws()
    {
        using var db = await SeedAsync();
        await DepositAsync(db, 1000m);
        var svc = Service(db);
        var ws = await svc.StartAsync(BookId, CashId, StatementDate, statementEndingBalance: 1000m);
        // Nothing cleared → outstanding 1000, book 1000, diff = 1000 − 1000 − 1000 = -1000 ≠ 0.

        var act = async () => await svc.FinalizeAsync(ws.ReconciliationId);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*out of balance*");
    }

    [Fact]
    public async Task SetCleared_OnFinalized_Throws()
    {
        using var db = await SeedAsync();
        await DepositAsync(db, 1000m);
        var depositLineId = (await db.JournalLines.Where(l => l.GlAccountId == CashId).SingleAsync(l => l.Debit == 1000m)).Id;
        var svc = Service(db);
        var ws = await svc.StartAsync(BookId, CashId, StatementDate, statementEndingBalance: 1000m);
        ws = await svc.SetClearedAsync(ws.ReconciliationId, depositLineId, true);
        await svc.FinalizeAsync(ws.ReconciliationId);

        var act = async () => await svc.SetClearedAsync(ws.ReconciliationId, depositLineId, false);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Draft*");
    }

    [Fact]
    public async Task FinalizedClearedLines_ExcludedFromNextRec()
    {
        using var db = await SeedAsync();
        await DepositAsync(db, 1000m, new DateOnly(2026, 5, 1));
        var depositLineId = (await db.JournalLines.Where(l => l.GlAccountId == CashId).SingleAsync(l => l.Debit == 1000m)).Id;
        var svc = Service(db);

        // Rec 1: clear + finalize the deposit.
        var ws1 = await svc.StartAsync(BookId, CashId, new DateOnly(2026, 5, 31), statementEndingBalance: 1000m);
        ws1 = await svc.SetClearedAsync(ws1.ReconciliationId, depositLineId, true);
        await svc.FinalizeAsync(ws1.ReconciliationId);

        // A later withdrawal; rec 2 should only see the new line (the deposit is settled).
        await WithdrawalAsync(db, 300m, new DateOnly(2026, 6, 15));
        var ws2 = await svc.StartAsync(BookId, CashId, new DateOnly(2026, 6, 30), statementEndingBalance: 1000m);

        ws2.Items.Should().ContainSingle();
        ws2.Items.Should().NotContain(i => i.JournalLineId == depositLineId);
    }
}
