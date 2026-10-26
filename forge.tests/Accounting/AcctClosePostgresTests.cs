using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Customers;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Postgres-backed checks of the conversion and book-settings paths: opening AR/AP open items checked by the
/// dry run and posted the way the opening open-items screen posts them (party lines on AR/AP control, the net
/// to Opening Balance Equity) appear in the AR and AP agings, tie to control, and land on the customer
/// statement; and a maker-checker threshold set through book settings sends a larger manual entry to
/// pending approval.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AcctClosePostgresTests(PostgresFixture fixture)
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int CashId = 100;
    private const int ArId = 110;
    private const int ApId = 200;
    private const int ObeId = 390;
    private const int RevenueId = 400;
    private static readonly DateOnly AsOf = new(2026, 1, 1);

    private static IHttpContextAccessor HttpAccessorFor(int userId) => new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test")),
        },
    };

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new AcctNumberSequenceAllocator(db), new SystemClock());

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

    private static Task ResyncSequencesAsync(AppDbContext db)
        => db.Database.ExecuteSqlRawAsync(@"
DO $$
DECLARE r RECORD;
BEGIN
  FOR r IN (SELECT c.table_name, pg_get_serial_sequence(quote_ident(c.table_name), 'id') AS seq
            FROM information_schema.columns c
            WHERE c.table_schema = 'public' AND c.column_name = 'id') LOOP
    IF r.seq IS NOT NULL THEN
      EXECUTE format('SELECT setval(%L, COALESCE((SELECT MAX(id) FROM %I), 0) + 1, false)', r.seq, r.table_name);
    END IF;
  END LOOP;
END $$;");

    private static async Task<(int CustomerId, int VendorId)> SeedAsync(AppDbContext db)
    {
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$", IsBaseCurrency = true, IsActive = true, DecimalPlaces = 2 });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = 10, BookId = BookId, Name = "FY2026",
            StartDate = AsOf, EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = 1000, FiscalYearId = 10, PeriodNumber = 1, Name = "FY2026",
            StartDate = AsOf, EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open,
        });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ArId, BookId = BookId, AccountNumber = "11000", Name = "Accounts Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true },
            new GlAccount { Id = ApId, BookId = BookId, AccountNumber = "20000", Name = "Accounts Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsControlAccount = true, ControlType = ControlAccountType.AP, IsPostable = true, IsActive = true },
            new GlAccount { Id = ObeId, BookId = BookId, AccountNumber = "39000", Name = "Opening Balance Equity", AccountType = AccountType.Equity, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = RevenueId, BookId = BookId, AccountNumber = "40000", Name = "Revenue", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = ArId },
            new AccountDeterminationRule { BookId = BookId, Key = "AP_CONTROL", GlAccountId = ApId },
            new AccountDeterminationRule { BookId = BookId, Key = "OPENING_BALANCE_EQUITY", GlAccountId = ObeId });
        var customer = new Customer { Name = "Acme Fabrication", CompanyName = "Acme Fabrication", CustomerNumber = "CUST-0001", IsActive = true };
        var vendor = new Vendor { CompanyName = "Steel Supply", VendorNumber = "VEND-0001", IsActive = true };
        await db.SaveChangesAsync();
        await ResyncSequencesAsync(db);
        db.Set<Customer>().Add(customer);
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        return (customer.Id, vendor.Id);
    }

    [Fact]
    public async Task OpeningOpenItems_ValidatedThenPosted_AppearInAgingsAndOnTheStatement()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        await SeedAsync(db);

        var check = await new ValidateOpeningOpenItemsHandler(db).Handle(
            new ValidateOpeningOpenItemsQuery(new ValidateOpeningOpenItemsRequestModel(BookId,
            [
                new(SubledgerPartyType.Customer, null, "CUST-0001", "OB-1001", new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31), 1200m, "Open invoice OB-1001"),
                new(SubledgerPartyType.Customer, null, "Acme Fabrication", null, null, null, 300m, null),
                new(SubledgerPartyType.Vendor, null, "VEND-0001", "V-77", null, null, 450m, "Open bill V-77"),
            ])),
            CancellationToken.None);
        check.ErrorCount.Should().Be(0);
        check.ReceivablesTotal.Should().Be(1500m);
        check.PayablesTotal.Should().Be(450m);

        var customerId = check.Rows[0].PartyId!.Value;
        var vendorId = check.Rows[2].PartyId!.Value;
        var lines = new List<OpeningBalanceLineModel>
        {
            new("AR_CONTROL", null, 1200m, 0m, SubledgerPartyType.Customer, customerId, "Open invoice OB-1001", "OB-1001", new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31)),
            new("AR_CONTROL", null, 300m, 0m, SubledgerPartyType.Customer, customerId, null),
            new("AP_CONTROL", null, 0m, 450m, SubledgerPartyType.Vendor, vendorId, "Open bill V-77", "V-77"),
            new("OPENING_BALANCE_EQUITY", null, 0m, 1050m, null, null, "Opening AR/AP offset"),
        };

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await new ConversionService(db, Engine(db), new BusinessIdentifierService(db, new SystemClock()))
                .PostOpeningBalancesAsync(new PostOpeningBalancesModel(BookId, AsOf, lines), postedByUserId: 7);
            await tx.CommitAsync();
        }

        await using var verify = fixture.CreateContext();
        var ar = await new ArAgingService(verify, new SystemClock()).GetArAgingAsync(BookId, new DateOnly(2026, 1, 15));
        ar.Customers.Should().ContainSingle(c => c.CustomerId == customerId).Which.OpenBalance.Should().Be(1500m);
        ar.Reconciliation.IsReconciled.Should().BeTrue();
        var ap = await new ApAgingService(verify, new SystemClock()).GetApAgingAsync(BookId, new DateOnly(2026, 1, 15));
        ap.Vendors.Should().ContainSingle(v => v.VendorId == vendorId).Which.OpenBalance.Should().Be(450m);
        ap.Reconciliation.IsReconciled.Should().BeTrue();

        (await verify.Invoices.Where(i => i.CustomerId == customerId && i.Status == InvoiceStatus.Sent).Select(i => i.InvoiceNumber).ToListAsync())
            .Should().Contain("OB-1001").And.HaveCount(2);
        QuestPDF.Settings.License = LicenseType.Community;
        var pdf = await new GenerateCustomerStatementHandler(verify, new SystemClock())
            .Handle(new GenerateCustomerStatementQuery(customerId), CancellationToken.None);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");

        var again = await new ValidateOpeningOpenItemsHandler(verify).Handle(
            new ValidateOpeningOpenItemsQuery(new ValidateOpeningOpenItemsRequestModel(BookId,
                [new(SubledgerPartyType.Customer, customerId, null, "OB-1001", null, null, 10m, null)])),
            CancellationToken.None);
        again.AlreadyPosted.Should().BeTrue();
        again.Rows[0].Errors.Should().Contain(ValidateOpeningOpenItemsHandler.DocumentNumberInUse);
    }

    [Fact]
    public async Task MakerCheckerThreshold_SetThroughBookSettings_SendsALargerEntryToPending()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        await SeedAsync(db);

        await new UpdateBookSettingsHandler(db)
            .Handle(new UpdateBookSettingsCommand(BookId, 1000m, null), CancellationToken.None);

        CreateManualJournalEntryCommand Entry(decimal amount) => new(
            BookId, new DateOnly(2026, 3, 10), UsdId, "Manual entry", false,
            [
                new CreateManualJournalLineModel(CashId, null, null, null, null, null, amount, 0m, null),
                new CreateManualJournalLineModel(RevenueId, null, null, null, null, null, 0m, amount, null),
            ]);

        var over = await new CreateManualJournalEntryHandler(Engine(db), HttpAccessorFor(42), db)
            .Handle(Entry(1500m), CancellationToken.None);
        var under = await new CreateManualJournalEntryHandler(Engine(db), HttpAccessorFor(42), db)
            .Handle(Entry(250m), CancellationToken.None);

        over.Status.Should().Be(JournalEntryStatus.PendingApproval.ToString());
        under.Status.Should().Be(JournalEntryStatus.Posted.ToString());

        await using var verify = fixture.CreateContext();
        (await verify.Books.SingleAsync()).MakerCheckerThreshold.Should().Be(1000m);
        (await new GetBookSettingsHandler(verify).Handle(new GetBookSettingsQuery(BookId), CancellationToken.None))
            .TimeZoneLocked.Should().BeTrue("a posted entry now fixes the book's time zone");
    }
}
