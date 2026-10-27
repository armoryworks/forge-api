using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Payments;
using Forge.Api.Features.Reports;
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
/// On real Postgres: a CAD marketplace payout settles a CAD invoice at the deposit-date rate and realizes the FX
/// difference like a customer payment; the sales-tax liability report reads the functional tax a foreign invoice
/// posted (and reports marketplace-collected tax apart so it still ties to Sales Tax Payable); the simple P&amp;L
/// built from the GL keeps freight income in revenue and FX gains in other income.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ForeignSettlementAndReportsPostgresTests(PostgresFixture fixture)
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int CadId = 2;
    private const int CashId = 300;
    private const int ArId = 301;
    private const int TaxId = 308;
    private const int RevenueId = 310;
    private const int FreightIncomeId = 311;
    private const int FxGainId = 312;
    private const int FeesId = 315;

    private static readonly (string Key, int Id, string Number, AccountType Type, bool Control, ControlAccountType? ControlType)[] Accounts =
    [
        ("CASH", CashId, "10100", AccountType.Asset, false, null),
        ("AR_CONTROL", ArId, "11000", AccountType.Asset, true, ControlAccountType.AR),
        ("MARKETPLACE_RESERVE", 302, "12500", AccountType.Asset, false, null),
        ("FX_REVALUATION", 303, "19900", AccountType.Asset, false, null),
        ("CUSTOMER_DEPOSITS", 307, "24000", AccountType.Liability, false, null),
        ("SALES_TAX_PAYABLE", TaxId, "23000", AccountType.Liability, false, null),
        ("DEFERRED_REVENUE", 309, "24500", AccountType.Liability, false, null),
        ("SALES_REVENUE", RevenueId, "40000", AccountType.Income, false, null),
        ("FREIGHT_INCOME", FreightIncomeId, "40500", AccountType.Income, false, null),
        ("SALES_RETURNS", 314, "41000", AccountType.Income, false, null),
        ("FX_GAIN", FxGainId, "90000", AccountType.Income, false, null),
        ("FX_LOSS", 313, "90100", AccountType.Expense, false, null),
        ("MARKETPLACE_FEES", FeesId, "61500", AccountType.Expense, false, null),
    ];

    private sealed class FullGlOn : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = true },
            DateTimeOffset.UtcNow);
        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

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

    private static Task SyncIdentitiesAsync(AppDbContext db)
        => db.Database.ExecuteSqlRawAsync(@"
DO $$
DECLARE r RECORD;
BEGIN
  FOR r IN (SELECT c.table_name, c.column_name FROM information_schema.columns c
            WHERE c.table_schema = 'public' AND c.is_identity = 'YES') LOOP
    EXECUTE format('SELECT setval(pg_get_serial_sequence(%L, %L), COALESCE((SELECT MAX(%I) FROM %I), 0) + 1, false)',
                   r.table_name, r.column_name, r.column_name, r.table_name);
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
            new ExchangeRate { FromCurrencyId = CadId, ToCurrencyId = UsdId, Rate = 0.75m, EffectiveDate = new DateOnly(2026, 2, 10), Source = ExchangeRateSource.Manual });
        await db.SaveChangesAsync();
        await SyncIdentitiesAsync(db);
    }

    private static async Task<(Customer Customer, SalesChannel Channel)> ChannelAsync(AppDbContext db)
    {
        var house = new Customer { Name = "Marketplace house account", CurrencyId = CadId, IsActive = true };
        db.Customers.Add(house);
        await db.SaveChangesAsync();
        var channel = new SalesChannel
        {
            Name = "Marketplace", Code = "MKT", ChannelType = SalesChannelType.Marketplace,
            SoldToCustomerId = house.Id, TaxCollectedBy = TaxCollectedBy.Marketplace,
        };
        db.SalesChannels.Add(channel);
        await db.SaveChangesAsync();
        return (house, channel);
    }

    private static async Task<Invoice> PostedInvoiceAsync(
        AppDbContext db, Customer customer, string number, decimal taxRate, int? salesOrderId, params InvoiceLine[] lines)
    {
        var invoice = new Invoice
        {
            InvoiceNumber = number, CustomerId = customer.Id, CurrencyId = CadId, FxRate = 0.73m,
            SalesOrderId = salesOrderId, Status = InvoiceStatus.Sent, TaxRate = taxRate,
            InvoiceDate = new DateTimeOffset(2026, 1, 15, 15, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 14, 15, 0, 0, TimeSpan.Zero),
            Lines = lines.ToList(),
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        await new InvoiceArPostingService(db, Engine(db), new FullGlOn()).PostInvoiceFinalizedAsync(invoice.Id, 7);
        return invoice;
    }

    private static async Task<SalesOrder> OrderAsync(AppDbContext db, Customer customer, SalesChannel channel, string number, TaxCollectedBy taxBy)
    {
        var order = new SalesOrder
        {
            OrderNumber = number, CustomerId = customer.Id, ChannelId = channel.Id, CurrencyId = CadId,
            Status = SalesOrderStatus.Confirmed, TaxCollectedBy = taxBy,
        };
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static async Task<decimal> FunctionalBalanceAsync(AppDbContext db, int accountId)
        => (await db.JournalLines.AsNoTracking().Where(l => l.GlAccountId == accountId).ToListAsync())
            .Sum(l => l.Debit > 0 ? l.FunctionalAmount : -l.FunctionalAmount);

    [Fact]
    public async Task A_cad_payout_settles_the_cad_invoice_at_the_deposit_rate_and_realizes_the_gain()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        await SeedAsync(db);
        var (house, channel) = await ChannelAsync(db);
        var order = await OrderAsync(db, house, channel, "MKT-1", TaxCollectedBy.Seller);
        var invoice = await PostedInvoiceAsync(db, house, "INV-CAD-1", 0m, order.Id,
            new InvoiceLine { Description = "Bracket kit", Quantity = 1m, UnitPrice = 1000m, LineNumber = 1 });

        var settlement = new ChannelSettlement
        {
            ChannelId = channel.Id, ExternalSettlementId = "STL-CAD-1", CurrencyCode = "CAD",
            PeriodStart = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            PeriodEnd = new DateTimeOffset(2026, 2, 14, 0, 0, 0, TimeSpan.Zero),
            DepositedAt = new DateTimeOffset(2026, 2, 15, 15, 0, 0, TimeSpan.Zero),
            ReportedNetAmount = 950m, Status = ChannelSettlementStatus.Reconciled,
            Lines =
            [
                new ChannelSettlementLine { LineType = ChannelSettlementLineType.OrderProceeds, Amount = 1000m, SalesOrderId = order.Id, ExternalOrderId = "EXT-1" },
                new ChannelSettlementLine { LineType = ChannelSettlementLineType.ReferralFee, Amount = -50m },
            ],
        };
        db.ChannelSettlements.Add(settlement);
        await db.SaveChangesAsync();

        var caps = new FullGlOn();
        await new ChannelSettlementPostingService(
                db, Engine(db), caps, new PaymentCashPostingService(db, Engine(db), caps), new PaymentRepository(db))
            .PostSettlementAsync(settlement.Id, 7);

        var payment = await db.Payments.AsNoTracking().Include(p => p.Applications).SingleAsync();
        payment.CurrencyId.Should().Be(CadId);
        payment.FxRate.Should().Be(0.75m);
        payment.Applications.Single().InvoiceId.Should().Be(invoice.Id);
        (await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id)).Status.Should().Be(InvoiceStatus.Paid);

        (await FunctionalBalanceAsync(db, ArId)).Should().Be(0m);
        (await FunctionalBalanceAsync(db, FxGainId)).Should().Be(-20m);
        (await FunctionalBalanceAsync(db, FeesId)).Should().Be(37.5m);
        (await FunctionalBalanceAsync(db, CashId)).Should().Be(712.5m);
        (await FunctionalBalanceAsync(db, 307)).Should().Be(0m);

        var pnl = await new GetSimplePnlReportHandler(
                new ReportRepository(db), db, new FinancialStatementService(db, new SystemClock()), caps)
            .Handle(new GetSimplePnlReportQuery(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 28, 0, 0, 0, TimeSpan.Zero)),
                CancellationToken.None);
        pnl.Where(i => i.Type == "Revenue").Sum(i => i.Amount).Should().Be(730m);
        pnl.Should().ContainSingle(i => i.Type == SimplePnlReportItem.OtherIncomeType && i.Amount == 20m);
    }

    [Fact]
    public async Task Tax_report_reads_functional_tax_and_ties_to_sales_tax_payable_with_marketplace_tax_apart()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        await SeedAsync(db);
        var (house, channel) = await ChannelAsync(db);
        var seller = await OrderAsync(db, house, channel, "MKT-2", TaxCollectedBy.Seller);
        var facilitated = await OrderAsync(db, house, channel, "MKT-3", TaxCollectedBy.Marketplace);

        await PostedInvoiceAsync(db, house, "INV-CAD-2", 0.10m, seller.Id,
            new InvoiceLine { Description = "Bracket kit", Quantity = 1m, UnitPrice = 1000m, LineNumber = 1 },
            new InvoiceLine { Description = "Freight", Quantity = 1m, UnitPrice = 37m, LineNumber = 2, LineType = DocumentLineType.Freight, TaxCode = InvoiceLine.NonTaxableCode });
        await PostedInvoiceAsync(db, house, "INV-CAD-3", 0.10m, facilitated.Id,
            new InvoiceLine { Description = "Bracket kit", Quantity = 1m, UnitPrice = 200m, LineNumber = 1 });

        var report = await new GetSalesTaxLiabilityHandler(db, new FullGlOn()).Handle(
            new GetSalesTaxLiabilityQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)), CancellationToken.None);

        report.TotalLiability.Should().Be(73m);
        report.MarketplaceCollectedTax.Should().Be(14.6m);
        (report.TotalLiability + report.MarketplaceCollectedTax).Should().Be(-await FunctionalBalanceAsync(db, TaxId));

        var pnl = await new GetSimplePnlReportHandler(
                new ReportRepository(db), db, new FinancialStatementService(db, new SystemClock()), new FullGlOn())
            .Handle(new GetSimplePnlReportQuery(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero)),
                CancellationToken.None);
        var glRevenue = -(await FunctionalBalanceAsync(db, RevenueId) + await FunctionalBalanceAsync(db, FreightIncomeId));
        pnl.Where(i => i.Type == "Revenue").Sum(i => i.Amount).Should().Be(glRevenue);
        pnl.Should().Contain(i => i.Category.StartsWith("40500") && i.Amount == 27.01m);
    }
}
