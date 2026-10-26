using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Invoices;
using Forge.Api.Features.Payments;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Multi-currency end to end on real Postgres: a CAD invoice booked at the rate on file and settled at a later
/// rate realizes the FX gain; a CAD purchase receipt is valued at the PO rate and the bill's rate difference
/// clears GRNI to purchase price variance; the period-end revaluation re-measures open CAD receivables.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MultiCurrencyPostgresTests(PostgresFixture fixture)
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int CadId = 2;

    private static readonly (string Key, int Id, string Number, AccountType Type, bool Control, ControlAccountType? ControlType)[] Accounts =
    [
        ("CASH", 300, "10100", AccountType.Asset, false, null),
        ("AR_CONTROL", 301, "11000", AccountType.Asset, true, ControlAccountType.AR),
        ("INVENTORY_RAW", 302, "13100", AccountType.Asset, true, ControlAccountType.Inventory),
        ("FX_REVALUATION", 303, "19900", AccountType.Asset, false, null),
        ("AP_CONTROL", 304, "20000", AccountType.Liability, true, ControlAccountType.AP),
        ("GRNI", 305, "21000", AccountType.Liability, false, null),
        ("FREIGHT_CLEARING", 306, "22000", AccountType.Liability, false, null),
        ("CUSTOMER_DEPOSITS", 307, "24000", AccountType.Liability, false, null),
        ("SALES_TAX_PAYABLE", 308, "23000", AccountType.Liability, false, null),
        ("DEFERRED_REVENUE", 309, "24500", AccountType.Liability, false, null),
        ("SALES_REVENUE", 310, "40000", AccountType.Income, false, null),
        ("PURCHASE_PRICE_VARIANCE", 311, "51000", AccountType.Expense, false, null),
        ("FX_GAIN", 312, "90000", AccountType.Income, false, null),
        ("FX_LOSS", 313, "90100", AccountType.Expense, false, null),
    ];

    private sealed class FullGlOn : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = true },
            DateTimeOffset.UtcNow);
        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static IHttpContextAccessor User7()
        => new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "7")])),
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

    private static async Task SeedAsync(AppDbContext db)
    {
        db.Set<Currency>().AddRange(
            new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$", DecimalPlaces = 2, IsBaseCurrency = true, IsActive = true },
            new Currency { Id = CadId, Code = "CAD", Name = "Canadian Dollar", Symbol = "$", DecimalPlaces = 2, IsActive = true });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = 10, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open,
        });
        for (var month = 1; month <= 12; month++)
        {
            var start = new DateOnly(2026, month, 1);
            db.Set<FiscalPeriod>().Add(new FiscalPeriod
            {
                Id = 1000 + month, FiscalYearId = 10, PeriodNumber = month, Name = $"P{month}",
                StartDate = start, EndDate = start.AddMonths(1).AddDays(-1), Status = FiscalPeriodStatus.Open,
            });
        }
        foreach (var (key, id, number, type, control, controlType) in Accounts)
        {
            db.Set<GlAccount>().Add(new GlAccount
            {
                Id = id, BookId = BookId, AccountNumber = number, Name = key, AccountType = type,
                NormalBalance = type is AccountType.Asset or AccountType.Expense ? NormalBalance.Debit : NormalBalance.Credit,
                IsControlAccount = control, ControlType = controlType, IsPostable = true, IsActive = true,
            });
            db.Set<AccountDeterminationRule>().Add(new AccountDeterminationRule { BookId = BookId, Key = key, GlAccountId = id });
        }
        db.Set<ExchangeRate>().AddRange(
            new ExchangeRate { FromCurrencyId = CadId, ToCurrencyId = UsdId, Rate = 0.73m, EffectiveDate = new DateOnly(2026, 1, 10), Source = ExchangeRateSource.Manual },
            new ExchangeRate { FromCurrencyId = CadId, ToCurrencyId = UsdId, Rate = 0.76m, EffectiveDate = new DateOnly(2026, 1, 31), Source = ExchangeRateSource.Manual },
            new ExchangeRate { FromCurrencyId = CadId, ToCurrencyId = UsdId, Rate = 0.75m, EffectiveDate = new DateOnly(2026, 2, 10), Source = ExchangeRateSource.Manual });
        await db.SaveChangesAsync();
    }

    private static async Task<(int CustomerId, int InvoiceId)> CadInvoiceAsync(AppDbContext db)
    {
        var customer = new Customer { Name = "Maple Fabrication", CurrencyId = CadId, IsActive = true };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var invoiceDate = new DateTimeOffset(2026, 1, 15, 15, 0, 0, TimeSpan.Zero);
        var created = await new CreateInvoiceHandler(new InvoiceRepository(db), new CustomerRepository(db), db).Handle(
            new CreateInvoiceCommand(
                customer.Id, null, null, invoiceDate, invoiceDate.AddDays(30), null, 0m, null,
                [new CreateInvoiceLineModel(null, "Machined brackets", 1, 1000m)]),
            CancellationToken.None);

        var invoice = await db.Invoices.SingleAsync(i => i.Id == created.Id);
        invoice.Status = InvoiceStatus.Sent;
        await db.SaveChangesAsync();
        await new InvoiceArPostingService(db, Engine(db), new FullGlOn()).PostInvoiceFinalizedAsync(invoice.Id, 7);
        return (customer.Id, invoice.Id);
    }

    [Fact]
    public async Task CadInvoice_BooksAtTheInvoiceRate_AndThePaymentRealizesTheGain()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        await SeedAsync(db);

        var (customerId, invoiceId) = await CadInvoiceAsync(db);

        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
        invoice.CurrencyId.Should().Be(CadId);
        invoice.FxRate.Should().Be(0.73m);
        var arItem = await db.ArOpenItems.AsNoTracking().SingleAsync(i => i.SourceId == invoiceId);
        arItem.OriginalFunctionalAmount.Should().Be(730m);

        var payment = await new CreatePaymentHandler(
                new PaymentRepository(db), new CustomerRepository(db), new InvoiceRepository(db), db,
                new PaymentCashPostingService(db, Engine(db), new FullGlOn()), User7())
            .Handle(new CreatePaymentCommand(
                customerId, "BankTransfer", 1000m, new DateTimeOffset(2026, 2, 15, 15, 0, 0, TimeSpan.Zero),
                "WIRE-1", null, [new CreatePaymentApplicationModel(invoiceId, 1000m)]), CancellationToken.None);

        payment.CurrencyCode.Should().Be("CAD");
        payment.FxRate.Should().Be(0.75m);

        var lines = await PaymentLinesAsync(db, payment.Id);
        lines.Should().ContainSingle(l => l.GlAccountId == 300 && l.Debit > 0 && l.FunctionalAmount == 750m);
        lines.Should().ContainSingle(l => l.GlAccountId == 301 && l.Credit > 0 && l.FunctionalAmount == 730m);
        lines.Should().ContainSingle(l => l.GlAccountId == 312 && l.Credit > 0 && l.FunctionalAmount == 20m);

        (await db.ArOpenItems.AsNoTracking().SingleAsync(i => i.SourceId == invoiceId)).Status.Should().Be(OpenItemStatus.Closed);
        (await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId)).Status.Should().Be(InvoiceStatus.Paid);
        (await FunctionalBalanceAsync(db, 301)).Should().Be(0m);
    }

    [Fact]
    public async Task CadReceipt_IsValuedAtThePoRate_AndTheBillRateDifferenceClearsGrniToPpv()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        await SeedAsync(db);

        var vendor = new Vendor { CompanyName = "Northern Steel", CurrencyId = CadId, IsActive = true };
        var part = new Part { PartNumber = "RAW-100", Name = "Bar stock", InventoryClass = InventoryClass.Raw };
        db.Vendors.Add(vendor);
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        var line = new PurchaseOrderLine { PartId = part.Id, Description = "Bar stock", OrderedQuantity = 100, ReceivedQuantity = 100, UnitPrice = 10m };
        var po = new PurchaseOrder
        {
            PONumber = "PO-1", VendorId = vendor.Id, Status = PurchaseOrderStatus.Received,
            CurrencyId = CadId, QuoteCurrency = "CAD", FxRate = 0.74m, Lines = [line],
        };
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync();
        db.Set<ReceivingRecord>().Add(new ReceivingRecord
        {
            PurchaseOrderLineId = line.Id, QuantityReceived = 100, ReceiptNumber = "R-20260115-0001", FxRate = 0.74m,
        });
        await db.SaveChangesAsync();

        await new ReceiptInventoryPostingService(db, Engine(db), new FullGlOn())
            .PostReceiptAsync(po.Id, "R-20260115-0001", new DateOnly(2026, 1, 15), 7);

        var receiptLines = await EntryLinesAsync(db, "Receipt", po.Id);
        receiptLines.Should().ContainSingle(l => l.GlAccountId == 305 && l.Credit == 740m);
        receiptLines.Should().ContainSingle(l => l.GlAccountId == 302 && l.Debit == 740m);

        var bill = new VendorBill
        {
            BillNumber = "BILL-1", VendorId = vendor.Id, PurchaseOrderId = po.Id, Status = VendorBillStatus.Approved,
            CurrencyId = CadId, FxRate = 0.72m,
            BillDate = new DateTimeOffset(2026, 1, 20, 15, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 19, 15, 0, 0, TimeSpan.Zero),
            Lines = [new VendorBillLine { Description = "Bar stock", Quantity = 100, UnitPrice = 10m, LineNumber = 1, PurchaseOrderLineId = line.Id, AccountDeterminationKey = "GRNI" }],
        };
        db.VendorBills.Add(bill);
        await db.SaveChangesAsync();

        await new VendorBillApPostingService(db, Engine(db), new FullGlOn()).PostVendorBillApprovedAsync(bill.Id, 7);

        var billLines = await EntryLinesAsync(db, "VendorBill", bill.Id);
        billLines.Should().ContainSingle(l => l.GlAccountId == 305 && l.Debit > 0 && l.FunctionalAmount == 720m);
        billLines.Should().ContainSingle(l => l.GlAccountId == 304 && l.Credit > 0 && l.FunctionalAmount == 720m);

        var fxLines = await EntryLinesAsync(db, "VendorBillFx", bill.Id);
        fxLines.Should().ContainSingle(l => l.GlAccountId == 305 && l.Debit == 20m);
        fxLines.Should().ContainSingle(l => l.GlAccountId == 311 && l.Credit == 20m);

        (await FunctionalBalanceAsync(db, 305)).Should().Be(0m);

        await new VendorBillApPostingService(db, Engine(db), new FullGlOn()).ReverseVendorBillApprovedAsync(bill.Id, 7);
        (await FunctionalBalanceAsync(db, 311)).Should().Be(0m);
        (await FunctionalBalanceAsync(db, 305)).Should().Be(-740m);
    }

    [Fact]
    public async Task RevalueAll_ReMeasuresOpenCadReceivables_AndAutoReverses()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        await SeedAsync(db);
        await CadInvoiceAsync(db);

        var service = new FxRevaluationService(db, Engine(db));
        var preview = await service.RevalueAllAsync(BookId, new DateOnly(2026, 1, 31), 7, preview: true);
        preview.Should().ContainSingle(r => r.CurrencyId == CadId && r.Adjustment == 30m && r.JournalEntryId == null);
        (await db.JournalEntries.CountAsync(e => e.SourceType == "FxRevaluation")).Should().Be(0);

        var results = await service.RevalueAllAsync(BookId, new DateOnly(2026, 1, 31), 7);

        var result = results.Should().ContainSingle().Which;
        result.Rate.Should().Be(0.76m);
        result.NetForeignPosition.Should().Be(1000m);
        result.CarryingFunctional.Should().Be(730m);
        result.Adjustment.Should().Be(30m);
        var entry = await db.JournalEntries.AsNoTracking().Include(e => e.Lines).SingleAsync(e => e.Id == result.JournalEntryId);
        entry.AutoReverseNextPeriod.Should().BeTrue();
        entry.Lines.Should().ContainSingle(l => l.GlAccountId == 303 && l.Debit == 30m);
        entry.Lines.Should().ContainSingle(l => l.GlAccountId == 312 && l.Credit == 30m);

        var runs = await new GetFxRevaluationsHandler(db).Handle(new GetFxRevaluationsQuery(BookId), CancellationToken.None);
        runs.Should().ContainSingle(r => r.CurrencyCode == "CAD" && r.Adjustment == 30m && r.AsOf == new DateOnly(2026, 1, 31));
    }

    private static Task<List<JournalLine>> PaymentLinesAsync(AppDbContext db, int paymentId)
        => EntryLinesAsync(db, "Payment", paymentId);

    private static Task<List<JournalLine>> EntryLinesAsync(AppDbContext db, string sourceType, int sourceId)
        => db.JournalLines.AsNoTracking()
            .Where(l => db.JournalEntries.Any(e => e.Id == l.JournalEntryId
                && e.SourceType == sourceType && e.SourceId == sourceId && e.ReversalOfEntryId == null))
            .ToListAsync();

    private static async Task<decimal> FunctionalBalanceAsync(AppDbContext db, int accountId)
        => (await db.JournalLines.AsNoTracking().Where(l => l.GlAccountId == accountId).ToListAsync())
            .Sum(l => l.Debit > 0 ? l.FunctionalAmount : -l.FunctionalAmount);
}
