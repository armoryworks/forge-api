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
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Phase-2 STAGE A.3 atomicity proof (the AP twin of <see cref="Phase1PostingAtomicityTests"/>): the
/// ApproveVendorBill and CreateVendorPayment handlers must commit the operational change and the inline
/// posting together — both or neither. Runs against real Postgres (Testcontainers / FORGE_TEST_PG) because
/// the InMemory provider ignores transactions. Each "rolls back" test forces the posting to fail after the
/// operational write (an omitted account-determination rule → PostingException) and asserts, via a fresh
/// context, that nothing survived.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Phase2ApHandlerAtomicityTests(PostgresFixture fixture)
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int ApControlId = 200;
    private const int OperatingExpenseId = 201;
    private const int CashId = 202;

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

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new AcctNumberSequenceAllocator(db), new SystemClock());

    private static ApproveVendorBillHandler ApproveHandler(AppDbContext db)
        => new(new VendorBillRepository(db),
            new VendorBillApPostingService(db, Engine(db), new FakeCapabilities(fullGlOn: true)),
            HttpContextFor(7), db);

    private static CreateVendorPaymentHandler PaymentHandler(AppDbContext db)
        => new(new VendorPaymentRepository(db), new VendorRepository(db), new VendorBillRepository(db), db,
            new VendorPaymentCashPostingService(db, Engine(db), new FakeCapabilities(fullGlOn: true)),
            HttpContextFor(7));

    private static Task ResetAsync(AppDbContext db)
        => db.Database.ExecuteSqlRawAsync(@"
DO $$
DECLARE r RECORD;
BEGIN
  FOR r IN (SELECT tablename FROM pg_tables
            WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory') LOOP
    EXECUTE 'TRUNCATE TABLE ' || quote_ident(r.tablename) || ' RESTART IDENTITY CASCADE';
  END LOOP;
END $$;");

    private async Task<int> SeedAccountingAsync(AppDbContext db, params string[] omitRuleKeys)
    {
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

        var rules = new[] { ("AP_CONTROL", ApControlId), ("OPERATING_EXPENSE", OperatingExpenseId), ("CASH", CashId) };
        foreach (var (key, accountId) in rules)
        {
            if (omitRuleKeys.Contains(key)) continue;
            db.Set<AccountDeterminationRule>().Add(new AccountDeterminationRule { BookId = BookId, Key = key, GlAccountId = accountId });
        }

        var vendor = new Vendor { CompanyName = "Delta Supply", IsActive = true };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        return vendor.Id;
    }

    private static async Task<VendorBill> AddBillAsync(AppDbContext db, int vendorId, VendorBillStatus status)
    {
        var bill = new VendorBill
        {
            BillNumber = "BILL-00001", VendorId = vendorId, Status = status,
            BillDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 14, 0, 0, 0, TimeSpan.Zero),
            Lines = [new VendorBillLine { Description = "Steel", Quantity = 2, UnitPrice = 50m, LineNumber = 1, AccountDeterminationKey = "OPERATING_EXPENSE" }],
        };
        db.Set<VendorBill>().Add(bill);
        await db.SaveChangesAsync();
        return bill;
    }

    [Fact]
    public async Task ApproveBill_postingFailure_rollsBackTheApproval()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        var vendorId = await SeedAccountingAsync(db, omitRuleKeys: "AP_CONTROL"); // AP credit leg fails to resolve
        var bill = await AddBillAsync(db, vendorId, VendorBillStatus.Draft);

        var act = async () => await ApproveHandler(db).Handle(new ApproveVendorBillCommand(bill.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("DETERMINATION_UNMAPPED");

        await using var verify = fixture.CreateContext();
        (await verify.VendorBills.SingleAsync(b => b.Id == bill.Id)).Status
            .Should().Be(VendorBillStatus.Draft, "the posting failure must roll the approval back");
        (await verify.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ApproveBill_postingSucceeds_commitsApprovalAndJournal()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        var vendorId = await SeedAccountingAsync(db);
        var bill = await AddBillAsync(db, vendorId, VendorBillStatus.Draft);

        await ApproveHandler(db).Handle(new ApproveVendorBillCommand(bill.Id), CancellationToken.None);

        await using var verify = fixture.CreateContext();
        (await verify.VendorBills.SingleAsync(b => b.Id == bill.Id)).Status.Should().Be(VendorBillStatus.Approved);
        var entry = await verify.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.SourceType.Should().Be("VendorBill");
        entry.Lines.Single(l => l.GlAccountId == ApControlId).Credit.Should().Be(100m);
    }

    [Fact]
    public async Task CreateVendorPayment_postingFailure_rollsBackThePayment()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        var vendorId = await SeedAccountingAsync(db, omitRuleKeys: "CASH"); // Cr Cash leg fails to resolve
        var bill = await AddBillAsync(db, vendorId, VendorBillStatus.Approved);

        var act = async () => await PaymentHandler(db).Handle(
            new CreateVendorPaymentCommand(vendorId, "Check", 100m,
                new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero), "REF-1", null,
                [new CreateVendorPaymentApplicationModel(bill.Id, 100m)]),
            CancellationToken.None);

        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("DETERMINATION_UNMAPPED");

        await using var verify = fixture.CreateContext();
        (await verify.VendorPayments.AnyAsync()).Should().BeFalse("the posting failure must roll the payment back");
        (await verify.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        // The bill's status flip must roll back too.
        (await verify.VendorBills.SingleAsync(b => b.Id == bill.Id)).Status.Should().Be(VendorBillStatus.Approved);
    }
}
