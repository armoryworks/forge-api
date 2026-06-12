using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using System.Security.Claims;

using Forge.Api.Capabilities;
using Forge.Api.Data;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Expenses;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Expense → bill promotion (the rectification of vendor-settled expenses): approving a
/// vendor-settled expense creates a born-Approved, expense-linked <see cref="VendorBill"/> so the
/// payable flows through the ONE AP pipeline — bill-keyed posting, ApOpenItem, aging, vendor
/// payments, void. Proves:
///   • gated on CAP-P2P-BILL (declines → caller falls back to the legacy expense AP posting);
///   • cash-settled / vendor-less expenses decline (legacy path unchanged);
///   • vendor-settled + Payables on → linked Approved bill; with FULLGL on it posts under the
///     BILL key (Dr expense w/ job tag / Cr AP party = vendor) + creates the open item; the legacy
///     Expense-keyed entry is never written;
///   • GL dark → the bill is still created (operationally payable), nothing posts;
///   • idempotent re-approve (one live bill, one entry, one open item);
///   • an expense whose payable already posted under the LEGACY Expense key (upgrade/backfill) is
///     promoted WITHOUT re-posting (no double-booked AP credit), and its demotion reverses the
///     legacy-keyed entry;
///   • demotion (expense leaves approved) voids the bill, reverses the posting, flips the open item
///     to Voided — and is blocked while vendor payments are applied;
///   • vendor PaymentTerms map onto CreditTerms for the due date;
///   • boot backfill reconstructs bills + open items for legacy-posted vendor-settled expenses,
///     idempotently, and never touches cash-settled or dark history.
/// </summary>
public class ExpenseBillPromotionTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int CashId = 100;
    private const int ApControlId = 101;
    private const int OperatingExpenseId = 102;

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    /// <summary>Capability snapshot with independent FULLGL / Payables toggles.</summary>
    private sealed class FakeCapabilities(bool fullGlOn, bool billOn = true) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["CAP-ACCT-FULLGL"] = fullGlOn,
                ["CAP-P2P-BILL"] = billOn,
            },
            DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static ExpenseBillPromotionService CreateService(AppDbContext db, bool fullGlOn, bool billOn = true)
    {
        var caps = new FakeCapabilities(fullGlOn, billOn);
        return new ExpenseBillPromotionService(
            db, new VendorBillRepository(db), new VendorBillApPostingService(db, Engine(db), caps), caps);
    }

    private static ExpenseApPostingService CreateLegacyService(AppDbContext db, bool fullGlOn)
        => new(db, Engine(db), new FakeCapabilities(fullGlOn));

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();

        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$", IsBaseCurrency = true });

        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
            RevenueRecognitionMethod = RevenueRecognitionMethod.PointInTime,
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

    private static async Task<int> AddVendorAsync(AppDbContext db, string? paymentTerms = null)
    {
        var vendor = new Vendor { CompanyName = "Delta Air Lines", IsActive = true, PaymentTerms = paymentTerms };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        return vendor.Id;
    }

    private static async Task<Expense> AddApprovedExpenseAsync(
        AppDbContext db, decimal amount, int? vendorId,
        ExpenseSettlementTarget? settlementTarget = null, int? jobId = null)
    {
        var expense = new Expense
        {
            UserId = 42,
            JobId = jobId,
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

    // ─────────────────────────── Promote ───────────────────────────

    [Fact]
    public async Task Promote_WhenBillCapabilityOff_DeclinesAndCreatesNothing()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var expense = await AddApprovedExpenseAsync(db, 250m, vendorId, ExpenseSettlementTarget.AccountsPayable);

        var bill = await CreateService(db, fullGlOn: true, billOn: false)
            .PromoteApprovedExpenseAsync(expense.Id, approvedByUserId: 7);

        bill.Should().BeNull();
        (await db.VendorBills.AnyAsync()).Should().BeFalse();
        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Promote_CashSettled_Declines()
    {
        using var db = await SeedAsync();
        var expense = await AddApprovedExpenseAsync(db, 100m, vendorId: null, ExpenseSettlementTarget.Cash);

        var bill = await CreateService(db, fullGlOn: true)
            .PromoteApprovedExpenseAsync(expense.Id, approvedByUserId: 7);

        bill.Should().BeNull();
        (await db.VendorBills.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Promote_VendorSettled_CreatesLinkedApprovedBill_PostsBillKey_CreatesOpenItem()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db, paymentTerms: "Net 30");
        var expense = await AddApprovedExpenseAsync(db, 250m, vendorId,
            ExpenseSettlementTarget.AccountsPayable, jobId: 5);

        var bill = await CreateService(db, fullGlOn: true)
            .PromoteApprovedExpenseAsync(expense.Id, approvedByUserId: 7);

        // The bill: born Approved, linked to the expense, one OPERATING_EXPENSE line with the job tag,
        // vendor terms mapped (Net 30 → due 30 days after the expense date).
        bill.Should().NotBeNull();
        bill!.Status.Should().Be(VendorBillStatus.Approved);
        bill.ExpenseId.Should().Be(expense.Id);
        bill.VendorId.Should().Be(vendorId);
        bill.Total.Should().Be(250m);
        bill.CreditTerms.Should().Be(CreditTerms.Net30);
        bill.DueDate.Should().Be(expense.ExpenseDate.AddDays(30));
        var line = bill.Lines.Single();
        line.AccountDeterminationKey.Should().Be("OPERATING_EXPENSE");
        line.JobId.Should().Be(5);

        // The GL: ONE entry, keyed to the BILL (the expense key is never written) — Dr expense
        // (job-tagged) / Cr AP, party = vendor.
        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.SourceType.Should().Be("VendorBill");
        entry.SourceId.Should().Be(bill.Id);
        entry.IdempotencyKey.Should().Be($"AP:VendorBill:{bill.Id}:BILL");
        entry.Status.Should().Be(JournalEntryStatus.Posted);
        var dr = entry.Lines.Single(l => l.GlAccountId == OperatingExpenseId);
        dr.Debit.Should().Be(250m);
        dr.JobId.Should().Be(5);
        var cr = entry.Lines.Single(l => l.GlAccountId == ApControlId);
        cr.Credit.Should().Be(250m);
        cr.SubledgerPartyType.Should().Be(SubledgerPartyType.Vendor);
        cr.SubledgerPartyId.Should().Be(vendorId);

        // The open item: bill-sourced, open for the full amount — Σ items == AP control.
        var item = await db.ApOpenItems.SingleAsync();
        item.SourceType.Should().Be("VendorBill");
        item.SourceId.Should().Be(bill.Id);
        item.VendorId.Should().Be(vendorId);
        item.OriginalFunctionalAmount.Should().Be(250m);
        item.Status.Should().Be(OpenItemStatus.Open);
    }

    [Fact]
    public async Task Promote_WhileGlDark_CreatesBillWithoutPosting()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var expense = await AddApprovedExpenseAsync(db, 99m, vendorId);

        var bill = await CreateService(db, fullGlOn: false)
            .PromoteApprovedExpenseAsync(expense.Id, approvedByUserId: 7);

        bill.Should().NotBeNull();
        bill!.Status.Should().Be(VendorBillStatus.Approved);
        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        (await db.ApOpenItems.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Promote_Reapprove_IsIdempotent()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var expense = await AddApprovedExpenseAsync(db, 250m, vendorId);
        var service = CreateService(db, fullGlOn: true);

        var first = await service.PromoteApprovedExpenseAsync(expense.Id, approvedByUserId: 7);
        var second = await service.PromoteApprovedExpenseAsync(expense.Id, approvedByUserId: 7);

        second!.Id.Should().Be(first!.Id);
        (await db.VendorBills.CountAsync()).Should().Be(1);
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await db.ApOpenItems.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Promote_LegacyPostedExpense_DoesNotDoublePostTheApCredit()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var expense = await AddApprovedExpenseAsync(db, 250m, vendorId, ExpenseSettlementTarget.AccountsPayable);

        // The payable entered the GL under the LEGACY Expense key (pre-promotion build).
        await CreateLegacyService(db, fullGlOn: true).PostExpenseApprovedAsync(expense.Id, approvedByUserId: 7);

        var bill = await CreateService(db, fullGlOn: true)
            .PromoteApprovedExpenseAsync(expense.Id, approvedByUserId: 7);

        // The bill exists, but NO second entry was posted — the AP credit stays single-booked.
        bill.Should().NotBeNull();
        var entry = await db.JournalEntries.IgnoreQueryFilters().SingleAsync();
        entry.SourceType.Should().Be("Expense");
        entry.Status.Should().Be(JournalEntryStatus.Posted);
    }

    // ─────────────────────────── Demote ───────────────────────────

    [Fact]
    public async Task Demote_VoidsBill_ReversesPosting_FlipsOpenItemToVoided()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var expense = await AddApprovedExpenseAsync(db, 250m, vendorId);
        var service = CreateService(db, fullGlOn: true);
        var bill = await service.PromoteApprovedExpenseAsync(expense.Id, approvedByUserId: 7);

        await service.DemoteExpenseBillAsync(expense.Id, actorUserId: 9);

        (await db.VendorBills.SingleAsync()).Status.Should().Be(VendorBillStatus.Void);

        // Original entry Reversed + an equal-and-opposite reversal entry → AP control nets to zero.
        var entries = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).ToListAsync();
        entries.Should().HaveCount(2);
        entries.Single(e => e.IdempotencyKey == $"AP:VendorBill:{bill!.Id}:BILL")
            .Status.Should().Be(JournalEntryStatus.Reversed);
        entries.SelectMany(e => e.Lines)
            .Where(l => l.GlAccountId == ApControlId)
            .Sum(l => l.Credit - l.Debit)
            .Should().Be(0m);

        (await db.ApOpenItems.SingleAsync()).Status.Should().Be(OpenItemStatus.Voided);
    }

    [Fact]
    public async Task Demote_WithPaymentsApplied_Throws()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var expense = await AddApprovedExpenseAsync(db, 250m, vendorId);
        var service = CreateService(db, fullGlOn: true);
        var bill = await service.PromoteApprovedExpenseAsync(expense.Id, approvedByUserId: 7);

        db.Set<VendorPaymentApplication>().Add(new VendorPaymentApplication
        {
            VendorBillId = bill!.Id, VendorPaymentId = 999, Amount = 100m,
        });
        await db.SaveChangesAsync();

        var act = () => service.DemoteExpenseBillAsync(expense.Id, actorUserId: 9);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*vendor payment(s) applied*");
        (await db.VendorBills.SingleAsync()).Status.Should().Be(VendorBillStatus.Approved);
    }

    [Fact]
    public async Task Demote_LegacyPostedBill_ReversesTheExpenseKeyedEntry()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var expense = await AddApprovedExpenseAsync(db, 250m, vendorId, ExpenseSettlementTarget.AccountsPayable);
        await CreateLegacyService(db, fullGlOn: true).PostExpenseApprovedAsync(expense.Id, approvedByUserId: 7);
        var service = CreateService(db, fullGlOn: true);
        await service.PromoteApprovedExpenseAsync(expense.Id, approvedByUserId: 7); // links, no re-post

        await service.DemoteExpenseBillAsync(expense.Id, actorUserId: 9);

        // The legacy Expense-keyed origination is what carries the payable → IT gets reversed.
        var original = await db.JournalEntries.IgnoreQueryFilters()
            .SingleAsync(e => e.SourceType == "Expense" && e.Status == JournalEntryStatus.Reversed);
        original.SourceId.Should().Be(expense.Id);
        (await db.VendorBills.SingleAsync()).Status.Should().Be(VendorBillStatus.Void);
    }

    [Fact]
    public async Task Demote_AfterDemotion_ReapproveCreatesAFreshBill()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var expense = await AddApprovedExpenseAsync(db, 250m, vendorId);
        var service = CreateService(db, fullGlOn: true);

        var first = await service.PromoteApprovedExpenseAsync(expense.Id, approvedByUserId: 7);
        await service.DemoteExpenseBillAsync(expense.Id, actorUserId: 9);
        var second = await service.PromoteApprovedExpenseAsync(expense.Id, approvedByUserId: 7);

        second!.Id.Should().NotBe(first!.Id);
        (await db.VendorBills.CountAsync(b => b.Status != VendorBillStatus.Void)).Should().Be(1);
        // Net AP control = the fresh promotion only (original + reversal + re-post).
        var apNet = (await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).ToListAsync())
            .SelectMany(e => e.Lines)
            .Where(l => l.GlAccountId == ApControlId)
            .Sum(l => l.Credit - l.Debit);
        apNet.Should().Be(250m);
    }

    // ─────────────────────────── Boot backfill ───────────────────────────

    [Fact]
    public async Task Backfill_ReconstructsBillAndOpenItem_ForLegacyPostedVendorExpense()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var vendorExpense = await AddApprovedExpenseAsync(db, 250m, vendorId, ExpenseSettlementTarget.AccountsPayable);
        var cashExpense = await AddApprovedExpenseAsync(db, 80m, vendorId: null, ExpenseSettlementTarget.Cash);

        var legacy = CreateLegacyService(db, fullGlOn: true);
        await legacy.PostExpenseApprovedAsync(vendorExpense.Id, approvedByUserId: 7);
        await legacy.PostExpenseApprovedAsync(cashExpense.Id, approvedByUserId: 7);

        await SeedData.EnsureExpenseBillsBackfilledAsync(db);

        // Only the vendor-settled expense gets a bill (the cash entry has no payable).
        var bill = await db.VendorBills.Include(b => b.Lines).SingleAsync();
        bill.ExpenseId.Should().Be(vendorExpense.Id);
        bill.Status.Should().Be(VendorBillStatus.Approved);
        bill.Total.Should().Be(250m);

        var item = await db.ApOpenItems.SingleAsync();
        item.SourceType.Should().Be("VendorBill");
        item.SourceId.Should().Be(bill.Id);
        item.OriginalFunctionalAmount.Should().Be(250m);
        item.Status.Should().Be(OpenItemStatus.Open);

        // No new GL — the legacy entries are untouched and remain the only ones.
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(2);

        // Idempotent: a second boot adds nothing.
        await SeedData.EnsureExpenseBillsBackfilledAsync(db);
        (await db.VendorBills.CountAsync()).Should().Be(1);
        (await db.ApOpenItems.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Backfill_DarkInstall_DoesNothing()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        await AddApprovedExpenseAsync(db, 250m, vendorId, ExpenseSettlementTarget.AccountsPayable);
        // Approved while FULLGL was off → no origination → no payable to reconstruct (out-of-band
        // history; auto-creating payables for it would risk double payment).

        await SeedData.EnsureExpenseBillsBackfilledAsync(db);

        (await db.VendorBills.AnyAsync()).Should().BeFalse();
        (await db.ApOpenItems.AnyAsync()).Should().BeFalse();
    }

    // ─────────────────────────── Vendor terms mapping ───────────────────────────

    [Theory]
    [InlineData(null, CreditTerms.DueOnReceipt, 0)]
    [InlineData("Net 15", CreditTerms.Net15, 15)]
    [InlineData("net30", CreditTerms.Net30, 30)]
    [InlineData("NET-45", CreditTerms.Net45, 45)]
    [InlineData("Net 60", CreditTerms.Net60, 60)]
    [InlineData("Net 90", CreditTerms.Net90, 90)]
    [InlineData("2/10 net 30 (custom)", CreditTerms.DueOnReceipt, 0)] // unrecognized → conservative
    public async Task Promote_MapsVendorPaymentTerms(string? terms, CreditTerms expected, int days)
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db, paymentTerms: terms);
        var expense = await AddApprovedExpenseAsync(db, 50m, vendorId);

        var bill = await CreateService(db, fullGlOn: false)
            .PromoteApprovedExpenseAsync(expense.Id, approvedByUserId: 7);

        bill!.CreditTerms.Should().Be(expected);
        bill.DueDate.Should().Be(expense.ExpenseDate.AddDays(days));
    }

    // ─────────────────────────── Handler wiring ───────────────────────────

    private static UpdateExpenseStatusHandler NewHandler(AppDbContext db, bool fullGlOn, int userId = 7)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())])),
        };
        return new UpdateExpenseStatusHandler(
            new ExpenseRepository(db),
            new HttpContextAccessor { HttpContext = ctx },
            new Mock<ISyncQueueRepository>().Object,
            new Mock<IAccountingProviderFactory>().Object,
            NullLogger<UpdateExpenseStatusHandler>.Instance,
            CreateLegacyService(db, fullGlOn),
            CreateService(db, fullGlOn),
            db);
    }

    [Fact]
    public async Task Handler_Approve_PromotesInsteadOfLegacyPosting()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var expense = new Expense
        {
            UserId = 42, Amount = 250m, Category = "Travel", Description = "Conference airfare",
            Status = ExpenseStatus.Pending, ExpenseDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero),
            SettlementTarget = ExpenseSettlementTarget.AccountsPayable, VendorId = vendorId,
        };
        db.Set<Expense>().Add(expense);
        await db.SaveChangesAsync();

        await NewHandler(db, fullGlOn: true).Handle(
            new UpdateExpenseStatusCommand(expense.Id,
                new Forge.Core.Models.UpdateExpenseStatusRequestModel(ExpenseStatus.Approved, null)),
            CancellationToken.None);

        // Promotion fired: linked bill, bill-keyed entry, and the legacy Expense key never written.
        var bill = await db.VendorBills.SingleAsync();
        bill.ExpenseId.Should().Be(expense.Id);
        var entry = await db.JournalEntries.IgnoreQueryFilters().SingleAsync();
        entry.SourceType.Should().Be("VendorBill");
    }

    [Fact]
    public async Task Handler_RejectAfterApprove_DemotesTheBill()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var expense = new Expense
        {
            UserId = 42, Amount = 250m, Category = "Travel", Description = "Conference airfare",
            Status = ExpenseStatus.Pending, ExpenseDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero),
            SettlementTarget = ExpenseSettlementTarget.AccountsPayable, VendorId = vendorId,
        };
        db.Set<Expense>().Add(expense);
        await db.SaveChangesAsync();
        var handler = NewHandler(db, fullGlOn: true);

        await handler.Handle(
            new UpdateExpenseStatusCommand(expense.Id,
                new Forge.Core.Models.UpdateExpenseStatusRequestModel(ExpenseStatus.Approved, null)),
            CancellationToken.None);
        await handler.Handle(
            new UpdateExpenseStatusCommand(expense.Id,
                new Forge.Core.Models.UpdateExpenseStatusRequestModel(ExpenseStatus.Rejected, "Duplicate of EXP-99")),
            CancellationToken.None);

        (await db.VendorBills.SingleAsync()).Status.Should().Be(VendorBillStatus.Void);
        // AP control nets to zero after the reversal — the rejected expense leaves no payable.
        var apNet = (await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).ToListAsync())
            .SelectMany(e => e.Lines)
            .Where(l => l.GlAccountId == ApControlId)
            .Sum(l => l.Credit - l.Debit);
        apNet.Should().Be(0m);
    }
}
