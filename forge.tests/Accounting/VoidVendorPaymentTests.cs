using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using System.Security.Claims;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.PaymentTransmissions;
using Forge.Api.Features.VendorPayments;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Void vendor payment (money-out integrity): the void drops bill applications (bills reopen with status
/// recomputed from the restored balance), reverses the cash-disbursement origination journal — netting AP
/// control AND cash-in-transit to zero for the payment — cancels any non-terminal bank transmission, and
/// soft-deletes the payment. Hard-blocked once the latest transmission Succeeded (money moved), and a
/// voided payment's transmission can never be manually re-queued.
/// </summary>
public class VoidVendorPaymentTests
{
    private const int UserId = 7;

    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int ApControlId = 200;
    private const int OperatingExpenseId = 201;
    private const int CashId = 202;
    private const int PrepaidExpenseId = 203;
    private const int CashInTransitId = 214;

    private static readonly DateTimeOffset Now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private sealed class FakeCapabilities(bool fullGlOn) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = fullGlOn },
            DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static IHttpContextAccessor HttpContextFor(int userId)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())])),
        };
        return new HttpContextAccessor { HttpContext = ctx };
    }

    private static VendorPaymentCashPostingService PostingService(AppDbContext db, bool fullGlOn)
        => new(db,
            new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new FakeAllocator(), new FixedClock(Now)),
            new FakeCapabilities(fullGlOn),
            clock: new FixedClock(Now));

    private static CreateVendorPaymentHandler CreateHandler(AppDbContext db, bool fullGlOn)
        => new(new VendorPaymentRepository(db), new VendorRepository(db), new VendorBillRepository(db), db,
            PostingService(db, fullGlOn), HttpContextFor(UserId));

    private static VoidVendorPaymentHandler VoidHandler(AppDbContext db, bool fullGlOn)
        => new(new VendorPaymentRepository(db), db, new FixedClock(Now),
            PostingService(db, fullGlOn), HttpContextFor(UserId));

    private static async Task<(AppDbContext db, int vendorId)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.CurrentUserId = UserId;

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
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open,
        });

        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = ApControlId, BookId = BookId, AccountNumber = "20000", Name = "Accounts Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsControlAccount = true, ControlType = ControlAccountType.AP, IsPostable = true, IsActive = true },
            new GlAccount { Id = OperatingExpenseId, BookId = BookId, AccountNumber = "60000", Name = "G&A", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = PrepaidExpenseId, BookId = BookId, AccountNumber = "12000", Name = "Prepaid Expenses", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = CashInTransitId, BookId = BookId, AccountNumber = "10150", Name = "Cash in Transit", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });

        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "AP_CONTROL", GlAccountId = ApControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "OPERATING_EXPENSE", GlAccountId = OperatingExpenseId },
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "PREPAID_EXPENSE", GlAccountId = PrepaidExpenseId },
            new AccountDeterminationRule { BookId = BookId, Key = "CASH_IN_TRANSIT", GlAccountId = CashInTransitId });

        var vendor = new Vendor { CompanyName = "Delta Supply", IsActive = true };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        return (db, vendor.Id);
    }

    private static async Task<VendorBill> AddApprovedBillAsync(AppDbContext db, int vendorId, decimal amount = 200m)
    {
        var bill = new VendorBill
        {
            BillNumber = $"BILL-{Guid.NewGuid():N}"[..12],
            VendorId = vendorId,
            Status = VendorBillStatus.Approved,
            BillDate = new DateTimeOffset(2026, 5, 20, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 6, 19, 0, 0, 0, TimeSpan.Zero),
            Lines = [new VendorBillLine { Description = "Steel", Quantity = 1m, UnitPrice = amount, LineNumber = 1, AccountDeterminationKey = "OPERATING_EXPENSE" }],
        };
        db.Set<VendorBill>().Add(bill);
        await db.SaveChangesAsync();
        return bill;
    }

    /// <summary>Creates a Wire payment fully applied to the bill (origination posts when FULLGL on).</summary>
    private static async Task<VendorPaymentListItemModel> CreateWirePaymentAsync(
        AppDbContext db, int vendorId, int billId, decimal amount, bool fullGlOn)
        => await CreateHandler(db, fullGlOn).Handle(
            new CreateVendorPaymentCommand(vendorId, "Wire", amount,
                new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), null, null,
                [new CreateVendorPaymentApplicationModel(billId, amount)]),
            CancellationToken.None);

    private static async Task<decimal> NetAsync(AppDbContext db, int accountId)
        => (await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).ToListAsync())
            .SelectMany(e => e.Lines)
            .Where(l => l.GlAccountId == accountId)
            .Sum(l => l.Debit - l.Credit);

    [Fact]
    public async Task Void_CancelsTransmission_ReversesGl_ReopensBill_SoftDeletes()
    {
        var (db, vendorId) = await SeedAsync();
        var bill = await AddApprovedBillAsync(db, vendorId);
        var created = await CreateWirePaymentAsync(db, vendorId, bill.Id, 200m, fullGlOn: true);

        (await db.VendorBills.SingleAsync(b => b.Id == bill.Id)).Status.Should().Be(VendorBillStatus.Paid);

        // Put the transmission mid-retry-cycle — a void must stop it cold.
        var transmission = await db.PaymentTransmissions.SingleAsync(t => t.SourceId == created.Id);
        transmission.Status = PaymentTransmissionStatus.Retrying;
        transmission.NextAttemptAt = Now.AddMinutes(4);
        await db.SaveChangesAsync();

        await VoidHandler(db, fullGlOn: true).Handle(
            new VoidVendorPaymentCommand(created.Id, "duplicate entry"), CancellationToken.None);

        // Transmission cancelled, never to fire again.
        transmission.Status.Should().Be(PaymentTransmissionStatus.Cancelled);
        transmission.NextAttemptAt.Should().BeNull();

        // GL fully unwound: origination + its reversal; AP control and CIT both net to zero.
        var entries = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).ToListAsync();
        entries.Should().HaveCount(2);
        entries.Should().Contain(e => e.Status == JournalEntryStatus.Reversed);
        (await NetAsync(db, ApControlId)).Should().Be(0m);
        (await NetAsync(db, CashInTransitId)).Should().Be(0m);

        // Bill reopened: application removed, balance restored, payable again.
        var reopened = await db.VendorBills
            .Include(b => b.Lines).Include(b => b.PaymentApplications)
            .SingleAsync(b => b.Id == bill.Id);
        reopened.Status.Should().Be(VendorBillStatus.Approved);
        reopened.PaymentApplications.Should().BeEmpty();
        reopened.BalanceDue.Should().Be(200m);

        // Payment soft-deleted (visible only past the global filter).
        (await db.VendorPayments.AnyAsync(p => p.Id == created.Id)).Should().BeFalse();
        var deleted = await db.VendorPayments.IgnoreQueryFilters().SingleAsync(p => p.Id == created.Id);
        deleted.DeletedAt.Should().NotBeNull();

        // Audit trail: "voided" on the payment + "payment-voided" on the bill.
        db.ActivityLogs.Should().Contain(a =>
            a.EntityType == "VendorPayment" && a.EntityId == created.Id && a.Action == "voided");
        db.ActivityLogs.Should().Contain(a =>
            a.EntityType == "VendorBill" && a.EntityId == bill.Id && a.Action == "payment-voided");
    }

    [Fact]
    public async Task Void_PartiallyVoided_BillDropsToPartiallyPaid()
    {
        var (db, vendorId) = await SeedAsync();
        var bill = await AddApprovedBillAsync(db, vendorId, amount: 200m);
        var first = await CreateWirePaymentAsync(db, vendorId, bill.Id, 120m, fullGlOn: false);
        var second = await CreateWirePaymentAsync(db, vendorId, bill.Id, 80m, fullGlOn: false);

        (await db.VendorBills.SingleAsync(b => b.Id == bill.Id)).Status.Should().Be(VendorBillStatus.Paid);

        await VoidHandler(db, fullGlOn: false).Handle(
            new VoidVendorPaymentCommand(second.Id, "overpaid"), CancellationToken.None);

        // The other payment's 120 is still applied → PartiallyPaid with 80 due again.
        var reopened = await db.VendorBills
            .Include(b => b.Lines).Include(b => b.PaymentApplications)
            .SingleAsync(b => b.Id == bill.Id);
        reopened.Status.Should().Be(VendorBillStatus.PartiallyPaid);
        reopened.AmountPaid.Should().Be(120m);
        reopened.BalanceDue.Should().Be(80m);
        (await db.VendorPayments.AnyAsync(p => p.Id == first.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Void_BlockedOnceTransmissionSucceeded()
    {
        var (db, vendorId) = await SeedAsync();
        var bill = await AddApprovedBillAsync(db, vendorId);
        var created = await CreateWirePaymentAsync(db, vendorId, bill.Id, 200m, fullGlOn: true);

        var transmission = await db.PaymentTransmissions.SingleAsync(t => t.SourceId == created.Id);
        transmission.Status = PaymentTransmissionStatus.Succeeded;
        await db.SaveChangesAsync();

        var act = async () => await VoidHandler(db, fullGlOn: true).Handle(
            new VoidVendorPaymentCommand(created.Id, "too late"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*money has been transmitted*");

        // Nothing changed: payment alive, bill still Paid, no reversal posted.
        (await db.VendorPayments.AnyAsync(p => p.Id == created.Id)).Should().BeTrue();
        (await db.VendorBills.SingleAsync(b => b.Id == bill.Id)).Status.Should().Be(VendorBillStatus.Paid);
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1); // origination only
    }

    [Fact]
    public async Task Retry_AfterVoid_IsRejected()
    {
        var (db, vendorId) = await SeedAsync();
        var bill = await AddApprovedBillAsync(db, vendorId);
        var created = await CreateWirePaymentAsync(db, vendorId, bill.Id, 200m, fullGlOn: false);

        var transmission = await db.PaymentTransmissions.SingleAsync(t => t.SourceId == created.Id);
        transmission.Status = PaymentTransmissionStatus.Failed;
        await db.SaveChangesAsync();

        await VoidHandler(db, fullGlOn: false).Handle(
            new VoidVendorPaymentCommand(created.Id, "bad vendor account"), CancellationToken.None);

        transmission.Status.Should().Be(PaymentTransmissionStatus.Cancelled); // void cancelled it

        var act = async () => await new RetryPaymentTransmissionHandler(db)
            .Handle(new RetryPaymentTransmissionCommand(transmission.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*payment has been voided*");
        transmission.Status.Should().Be(PaymentTransmissionStatus.Cancelled);
    }

    [Fact]
    public async Task Void_FullGlOff_StillVoidsOperationally_NoJournals()
    {
        var (db, vendorId) = await SeedAsync();
        var bill = await AddApprovedBillAsync(db, vendorId);
        var created = await CreateWirePaymentAsync(db, vendorId, bill.Id, 200m, fullGlOn: false);

        await VoidHandler(db, fullGlOn: false).Handle(
            new VoidVendorPaymentCommand(created.Id, "wrong amount"), CancellationToken.None);

        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        (await db.VendorPayments.AnyAsync(p => p.Id == created.Id)).Should().BeFalse();
        (await db.VendorBills.SingleAsync(b => b.Id == bill.Id)).Status.Should().Be(VendorBillStatus.Approved);
    }

    [Fact]
    public async Task Void_EmptyReason_Throws()
    {
        var (db, _) = await SeedAsync();

        var act = async () => await VoidHandler(db, fullGlOn: false).Handle(
            new VoidVendorPaymentCommand(1, "   "), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*reason is required*");
    }

    [Fact]
    public async Task Void_NotFound_Throws_AndSecondVoidIsNotFoundToo()
    {
        var (db, vendorId) = await SeedAsync();

        var missing = async () => await VoidHandler(db, fullGlOn: false).Handle(
            new VoidVendorPaymentCommand(999, "x"), CancellationToken.None);
        await missing.Should().ThrowAsync<KeyNotFoundException>();

        // A voided payment is soft-deleted → the global filter hides it → a re-void is a 404, not a dup.
        var bill = await AddApprovedBillAsync(db, vendorId);
        var created = await CreateWirePaymentAsync(db, vendorId, bill.Id, 200m, fullGlOn: false);
        await VoidHandler(db, fullGlOn: false).Handle(
            new VoidVendorPaymentCommand(created.Id, "first"), CancellationToken.None);

        var again = async () => await VoidHandler(db, fullGlOn: false).Handle(
            new VoidVendorPaymentCommand(created.Id, "second"), CancellationToken.None);
        await again.Should().ThrowAsync<KeyNotFoundException>();
    }
}
