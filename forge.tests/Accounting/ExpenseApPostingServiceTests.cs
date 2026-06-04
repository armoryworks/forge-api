using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Phase-1 STAGE C — Expense / AP posting wired into the approved transition of
/// the UpdateExpenseStatus flow (ACCOUNTING_SUITE_PLAN §7 matrix row "Expense
/// approved"). Proves:
///   • DARK by default — a no-op while CAP-ACCT-FULLGL is OFF (zero behavior change);
///   • vendor-settled expense → Dr OPERATING_EXPENSE / Cr AP_CONTROL (party = vendor);
///   • cash-settled expense (no vendor / Cash target) → Dr OPERATING_EXPENSE / Cr CASH;
///   • a null SettlementTarget infers AP when a VendorId is present, else Cash;
///   • a vendor-settled expense with no vendor is a hard PostingException (control-line party, §5.2);
///   • idempotent — a re-approve returns the existing entry (no duplicate).
/// </summary>
public class ExpenseApPostingServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;

    private const int CashId = 100;
    private const int ApControlId = 101;
    private const int OperatingExpenseId = 102;

    private const int OpenPeriodId = 1000;

    /// <summary>In-process allocator (InMemory can't run the row-lock SQL).</summary>
    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    /// <summary>Toggleable capability snapshot provider for the FULLGL gate.</summary>
    private sealed class FakeCapabilities(bool fullGlOn) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = fullGlOn },
            DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static ExpenseApPostingService CreateService(AppDbContext db, bool fullGlOn)
        => new(
            db,
            new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock()),
            new FakeCapabilities(fullGlOn));

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();

        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });

        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
            RevenueRecognitionMethod = RevenueRecognitionMethod.PointInTime,
        });

        var fy = new FiscalYear
        {
            Id = FiscalYearId, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalYearStatus.Open,
        };
        db.Set<FiscalYear>().Add(fy);
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "Jan 2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31),
            Status = FiscalPeriodStatus.Open,
        });

        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ApControlId, BookId = BookId, AccountNumber = "20000", Name = "Accounts Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsControlAccount = true, ControlType = ControlAccountType.AP, IsPostable = true, IsActive = true },
            new GlAccount { Id = OperatingExpenseId, BookId = BookId, AccountNumber = "60000", Name = "General & Administrative", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });

        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "AP_CONTROL", GlAccountId = ApControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "OPERATING_EXPENSE", GlAccountId = OperatingExpenseId });

        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>
    /// Adds an approved expense with the given settlement disambiguators. The
    /// posting service reads SettlementTarget + VendorId off the Expense.
    /// </summary>
    private static async Task<Expense> AddExpenseAsync(
        AppDbContext db,
        decimal amount,
        ExpenseSettlementTarget? settlementTarget = null,
        int? vendorId = null)
    {
        var expense = new Expense
        {
            UserId = 42,
            Amount = amount,
            Category = "Travel",
            Description = "Conference airfare",
            Status = ExpenseStatus.Approved,
            ApprovedBy = 7,
            ExpenseDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero),
            SettlementTarget = settlementTarget,
            VendorId = vendorId,
        };
        db.Set<Expense>().Add(expense);
        await db.SaveChangesAsync();
        return expense;
    }

    private static async Task<int> AddVendorAsync(AppDbContext db, string name = "Delta Air Lines")
    {
        var vendor = new Vendor { CompanyName = name, IsActive = true };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        return vendor.Id;
    }

    [Fact]
    public async Task Post_WhenFullGlOff_IsNoOp()
    {
        using var db = await SeedAsync();
        var expense = await AddExpenseAsync(db, amount: 100m, settlementTarget: ExpenseSettlementTarget.Cash);
        var service = CreateService(db, fullGlOn: false);

        await service.PostExpenseApprovedAsync(expense.Id, approvedByUserId: 7);

        // Dark by default: nothing posted, no ledger movement at all.
        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        (await db.LedgerBalances.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Post_WhenFullGlOn_VendorSettled_PostsExpenseAndApWithVendorParty()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var expense = await AddExpenseAsync(db, amount: 250m,
            settlementTarget: ExpenseSettlementTarget.AccountsPayable, vendorId: vendorId);
        var service = CreateService(db, fullGlOn: true);

        await service.PostExpenseApprovedAsync(expense.Id, approvedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Source.Should().Be(JournalSource.AP);
        entry.SourceType.Should().Be("Expense");
        entry.SourceId.Should().Be(expense.Id);
        entry.EntryDate.Should().Be(new DateOnly(2026, 1, 20));
        entry.Status.Should().Be(JournalEntryStatus.Posted);
        entry.PostedBy.Should().Be(7);

        // Dr Expense for the full amount.
        entry.Lines.Single(l => l.GlAccountId == OperatingExpenseId).Debit.Should().Be(250m);

        // Cr AP for the full amount, party = vendor.
        var ap = entry.Lines.Single(l => l.GlAccountId == ApControlId);
        ap.Credit.Should().Be(250m);
        ap.SubledgerPartyType.Should().Be(SubledgerPartyType.Vendor);
        ap.SubledgerPartyId.Should().Be(vendorId);

        // No cash leg when settling to AP.
        entry.Lines.Should().NotContain(l => l.GlAccountId == CashId);

        // Balanced.
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Post_WhenFullGlOn_CashSettled_PostsExpenseAndCash()
    {
        using var db = await SeedAsync();
        var expense = await AddExpenseAsync(db, amount: 80m, settlementTarget: ExpenseSettlementTarget.Cash);
        var service = CreateService(db, fullGlOn: true);

        await service.PostExpenseApprovedAsync(expense.Id, approvedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == OperatingExpenseId).Debit.Should().Be(80m);
        entry.Lines.Single(l => l.GlAccountId == CashId).Credit.Should().Be(80m);
        // No AP leg when settling to cash.
        entry.Lines.Should().NotContain(l => l.GlAccountId == ApControlId);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Post_WhenFullGlOn_NullTargetWithVendor_InfersAp()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        // No explicit target; presence of a vendor implies AP.
        var expense = await AddExpenseAsync(db, amount: 120m, settlementTarget: null, vendorId: vendorId);
        var service = CreateService(db, fullGlOn: true);

        await service.PostExpenseApprovedAsync(expense.Id, approvedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        var ap = entry.Lines.Single(l => l.GlAccountId == ApControlId);
        ap.Credit.Should().Be(120m);
        ap.SubledgerPartyId.Should().Be(vendorId);
        entry.Lines.Should().NotContain(l => l.GlAccountId == CashId);
    }

    [Fact]
    public async Task Post_WhenFullGlOn_NullTargetNoVendor_InfersCash()
    {
        using var db = await SeedAsync();
        // No target, no vendor → cash.
        var expense = await AddExpenseAsync(db, amount: 45m, settlementTarget: null, vendorId: null);
        var service = CreateService(db, fullGlOn: true);

        await service.PostExpenseApprovedAsync(expense.Id, approvedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == CashId).Credit.Should().Be(45m);
        entry.Lines.Should().NotContain(l => l.GlAccountId == ApControlId);
    }

    [Fact]
    public async Task Post_WhenFullGlOn_ApTargetButNoVendor_Throws()
    {
        using var db = await SeedAsync();
        // Explicit AP target but no vendor to carry as the control-line party.
        var expense = await AddExpenseAsync(db, amount: 60m,
            settlementTarget: ExpenseSettlementTarget.AccountsPayable, vendorId: null);
        var service = CreateService(db, fullGlOn: true);

        var act = async () => await service.PostExpenseApprovedAsync(expense.Id, approvedByUserId: 7);

        (await act.Should().ThrowAsync<PostingException>())
            .Which.Code.Should().Be("EXPENSE_AP_NO_VENDOR");

        // Nothing committed on the failed path.
        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Post_CalledTwice_IsIdempotent()
    {
        using var db = await SeedAsync();
        var expense = await AddExpenseAsync(db, amount: 100m, settlementTarget: ExpenseSettlementTarget.Cash);
        var service = CreateService(db, fullGlOn: true);

        await service.PostExpenseApprovedAsync(expense.Id, approvedByUserId: 7);
        await service.PostExpenseApprovedAsync(expense.Id, approvedByUserId: 7);

        // A re-approve returns the existing entry — no duplicate journal.
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }
}
