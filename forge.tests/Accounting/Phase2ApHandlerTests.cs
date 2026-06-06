using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using System.Security.Claims;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.VendorBills;
using Forge.Api.Features.VendorPayments;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Phase-2 STAGE A.3 — the AP command handlers (CreateVendorBill → ApproveVendorBill → CreateVendorPayment)
/// exercised end-to-end on the InMemory provider. Proves the operational create/approve/pay flow and that,
/// with CAP-ACCT-FULLGL on, approval posts the AP/expense journal and payment posts the AP/cash journal
/// (the inline posting wiring). Transaction-atomicity (rollback) is proven separately on Postgres
/// (<see cref="Phase2ApHandlerAtomicityTests"/>) — InMemory ignores transactions.
/// </summary>
public class Phase2ApHandlerTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int ApControlId = 200;
    private const int OperatingExpenseId = 201;
    private const int CashId = 202;

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

    private sealed class Harness(AppDbContext db, bool fullGlOn)
    {
        private readonly ForgeGlPostingEngine _engine =
            new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

        public CreateVendorBillHandler CreateBill { get; } =
            new(new VendorBillRepository(db), new VendorRepository(db));

        public ApproveVendorBillHandler Approve { get; } =
            new(new VendorBillRepository(db),
                new VendorBillApPostingService(db,
                    new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock()),
                    new FakeCapabilities(fullGlOn)),
                HttpContextFor(7), db);

        public CreateVendorPaymentHandler CreatePayment { get; } =
            new(new VendorPaymentRepository(db), new VendorRepository(db), new VendorBillRepository(db), db,
                new VendorPaymentCashPostingService(db,
                    new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock()),
                    new FakeCapabilities(fullGlOn)),
                HttpContextFor(7));
    }

    private static async Task<(AppDbContext db, int vendorId)> SeedAsync()
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
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open,
        });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = ApControlId, BookId = BookId, AccountNumber = "20000", Name = "Accounts Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsControlAccount = true, ControlType = ControlAccountType.AP, IsPostable = true, IsActive = true },
            new GlAccount { Id = OperatingExpenseId, BookId = BookId, AccountNumber = "60000", Name = "G&A", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "AP_CONTROL", GlAccountId = ApControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "OPERATING_EXPENSE", GlAccountId = OperatingExpenseId },
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId });

        var vendor = new Vendor { CompanyName = "Delta Supply", IsActive = true };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        return (db, vendor.Id);
    }

    private static CreateVendorBillCommand BillCmd(int vendorId, decimal unit1 = 100m, decimal unit2 = 100m)
        => new(vendorId, "V-555", null,
            new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 14, 0, 0, 0, TimeSpan.Zero),
            0m, "note",
            [
                new CreateVendorBillLineModel(null, "Steel", 1, unit1, null),
                new CreateVendorBillLineModel(null, "Fasteners", 1, unit2, "OPERATING_EXPENSE"),
            ]);

    [Fact]
    public async Task FullGlOn_CreateApprovePay_PostsBillAndPaymentJournals()
    {
        var (db, vendorId) = await SeedAsync();
        var h = new Harness(db, fullGlOn: true);

        var bill = await h.CreateBill.Handle(BillCmd(vendorId), CancellationToken.None);
        bill.Status.Should().Be("Draft");
        bill.Total.Should().Be(200m);
        // Creating a Draft is not a posting trigger.
        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();

        await h.Approve.Handle(new ApproveVendorBillCommand(bill.Id), CancellationToken.None);

        var billEntry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .SingleAsync(e => e.SourceType == "VendorBill");
        billEntry.Source.Should().Be(JournalSource.AP);
        billEntry.Lines.Where(l => l.GlAccountId == OperatingExpenseId).Sum(l => l.Debit).Should().Be(200m);
        billEntry.Lines.Single(l => l.GlAccountId == ApControlId).Credit.Should().Be(200m);
        billEntry.Lines.Single(l => l.GlAccountId == ApControlId).SubledgerPartyId.Should().Be(vendorId);

        var payment = await h.CreatePayment.Handle(
            new CreateVendorPaymentCommand(vendorId, "Check", 200m,
                new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero), "REF-1", null,
                [new CreateVendorPaymentApplicationModel(bill.Id, 200m)]),
            CancellationToken.None);
        payment.AppliedAmount.Should().Be(200m);

        var payEntry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .SingleAsync(e => e.SourceType == "VendorPayment");
        payEntry.Lines.Single(l => l.GlAccountId == ApControlId).Debit.Should().Be(200m);
        payEntry.Lines.Single(l => l.GlAccountId == CashId).Credit.Should().Be(200m);

        // The bill is fully settled.
        (await db.VendorBills.SingleAsync(b => b.Id == bill.Id)).Status.Should().Be(VendorBillStatus.Paid);
    }

    [Fact]
    public async Task FullGlOff_CreateApprovePay_NoJournals_ButStatusesTransition()
    {
        var (db, vendorId) = await SeedAsync();
        var h = new Harness(db, fullGlOn: false);

        var bill = await h.CreateBill.Handle(BillCmd(vendorId), CancellationToken.None);
        await h.Approve.Handle(new ApproveVendorBillCommand(bill.Id), CancellationToken.None);
        await h.CreatePayment.Handle(
            new CreateVendorPaymentCommand(vendorId, "Check", 200m,
                new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero), null, null,
                [new CreateVendorPaymentApplicationModel(bill.Id, 200m)]),
            CancellationToken.None);

        // Dark: zero ledger movement, but the operational state machine still advances.
        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        (await db.VendorBills.SingleAsync(b => b.Id == bill.Id)).Status.Should().Be(VendorBillStatus.Paid);
    }

    [Fact]
    public async Task ApproveBill_AlreadyApproved_Throws()
    {
        var (db, vendorId) = await SeedAsync();
        var h = new Harness(db, fullGlOn: false);

        var bill = await h.CreateBill.Handle(BillCmd(vendorId), CancellationToken.None);
        await h.Approve.Handle(new ApproveVendorBillCommand(bill.Id), CancellationToken.None);

        var act = async () => await h.Approve.Handle(new ApproveVendorBillCommand(bill.Id), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Only Draft bills*");
    }

    [Fact]
    public async Task CreatePayment_OverApply_Throws()
    {
        var (db, vendorId) = await SeedAsync();
        var h = new Harness(db, fullGlOn: false);

        var bill = await h.CreateBill.Handle(BillCmd(vendorId), CancellationToken.None);
        await h.Approve.Handle(new ApproveVendorBillCommand(bill.Id), CancellationToken.None);

        // Bill balance is 200; applying 300 over-applies.
        var act = async () => await h.CreatePayment.Handle(
            new CreateVendorPaymentCommand(vendorId, "Check", 300m,
                new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero), null, null,
                [new CreateVendorPaymentApplicationModel(bill.Id, 300m)]),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds bill*");
    }

    [Fact]
    public async Task CreatePayment_AgainstDraftBill_Throws()
    {
        var (db, vendorId) = await SeedAsync();
        var h = new Harness(db, fullGlOn: false);
        // Bill is Draft (NOT approved) → its AP credit was never booked.
        var bill = await h.CreateBill.Handle(BillCmd(vendorId), CancellationToken.None);

        var act = async () => await h.CreatePayment.Handle(
            new CreateVendorPaymentCommand(vendorId, "Check", 200m,
                new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero), null, null,
                [new CreateVendorPaymentApplicationModel(bill.Id, 200m)]),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*only Approved or PartiallyPaid*");
    }

    [Fact]
    public async Task CreatePayment_BillBelongsToDifferentVendor_Throws()
    {
        var (db, vendorId) = await SeedAsync();
        var h = new Harness(db, fullGlOn: false);
        var bill = await h.CreateBill.Handle(BillCmd(vendorId), CancellationToken.None);
        await h.Approve.Handle(new ApproveVendorBillCommand(bill.Id), CancellationToken.None);

        var other = new Vendor { CompanyName = "Other Supply", IsActive = true };
        db.Set<Vendor>().Add(other);
        await db.SaveChangesAsync();

        var act = async () => await h.CreatePayment.Handle(
            new CreateVendorPaymentCommand(other.Id, "Check", 200m,
                new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero), null, null,
                [new CreateVendorPaymentApplicationModel(bill.Id, 200m)]),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different vendor*");
    }

    [Fact]
    public void Validator_InvalidMethod_FailsValidation()
    {
        var result = new CreateVendorPaymentValidator().Validate(
            new CreateVendorPaymentCommand(1, "Venmo", 100m, default, null, null, null));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateVendorPaymentCommand.Method));
    }

    [Fact]
    public void Validator_DuplicateBillApplication_FailsValidation()
    {
        var result = new CreateVendorPaymentValidator().Validate(
            new CreateVendorPaymentCommand(1, "Check", 200m, default, null, null,
                [new CreateVendorPaymentApplicationModel(5, 100m), new CreateVendorPaymentApplicationModel(5, 100m)]));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("at most once"));
    }

    [Fact]
    public void Validator_ZeroTotalBill_FailsValidation()
    {
        var result = new CreateVendorBillValidator().Validate(
            new CreateVendorBillCommand(1, null, null, default, default, 0m, null,
                [new CreateVendorBillLineModel(null, "Free sample", 1, 0m, null)]));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("greater than zero"));
    }
}
