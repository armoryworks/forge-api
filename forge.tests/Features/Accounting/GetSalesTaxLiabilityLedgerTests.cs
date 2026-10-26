using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Features.Accounting;

/// <summary>
/// The sales-tax liability report with the built-in GL on: it reads the tax each invoice posted, so its
/// total equals the Sales Tax Payable credits for the period, including per-line rounding, a foreign-currency
/// invoice and a voided invoice.
/// </summary>
public class GetSalesTaxLiabilityLedgerTests : IDisposable
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int CadId = 2;
    private const int ArId = 110;
    private const int RevenueId = 400;
    private const int DeferredId = 240;
    private const int TaxPayableId = 230;
    private const string FullGl = "CAP-ACCT-FULLGL";

    private static readonly DateTimeOffset January = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private sealed class Allocator(AppDbContext db) : IAcctNumberSequenceAllocator
    {
        public async Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => (await db.JournalEntries.IgnoreQueryFilters()
                .Where(e => e.BookId == bookId && e.FiscalYearId == fiscalYearId)
                .MaxAsync(e => (long?)e.EntryNumber, ct) ?? 0) + 1;
    }

    private InvoiceArPostingService Posting() => new(
        _db,
        new ForgeGlPostingEngine(_db, new AccountDeterminationResolver(_db), new Allocator(_db), new SystemClock()),
        new StubCapabilitySnapshotProvider(FullGl));

    private GetSalesTaxLiabilityHandler Handler() => new(_db, new StubCapabilitySnapshotProvider(FullGl));

    private async Task SeedBookAsync()
    {
        _db.Set<Currency>().AddRange(
            new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" },
            new Currency { Id = CadId, Code = "CAD", Name = "Canadian Dollar", Symbol = "$" });
        _db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/Denver", RoundingTolerance = 0.01m, IsActive = true,
            RevenueRecognitionMethod = RevenueRecognitionMethod.PointInTime,
        });
        _db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = 10, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open,
        });
        _db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = 1000, FiscalYearId = 10, PeriodNumber = 1, Name = "Jan 2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31), Status = FiscalPeriodStatus.Open,
        });
        _db.Set<GlAccount>().AddRange(
            new GlAccount { Id = ArId, BookId = BookId, AccountNumber = "11000", Name = "Accounts Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true },
            new GlAccount { Id = RevenueId, BookId = BookId, AccountNumber = "40000", Name = "Sales Revenue", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = DeferredId, BookId = BookId, AccountNumber = "24000", Name = "Deferred Revenue", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = TaxPayableId, BookId = BookId, AccountNumber = "23000", Name = "Sales Tax Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });
        _db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = ArId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = RevenueId },
            new AccountDeterminationRule { BookId = BookId, Key = "DEFERRED_REVENUE", GlAccountId = DeferredId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_TAX_PAYABLE", GlAccountId = TaxPayableId });
        await _db.SaveChangesAsync();
    }

    private async Task<Invoice> PostInvoiceAsync(
        string state, decimal taxRate, IEnumerable<(decimal Qty, decimal Price)> lines,
        int currencyId = UsdId, decimal fxRate = 1m, TaxCollectedBy collectedBy = TaxCollectedBy.Seller)
    {
        var customer = new Customer { Name = $"Buyer {state}" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        var order = new SalesOrder
        {
            OrderNumber = $"SO-{Guid.NewGuid():N}",
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Confirmed,
            TaxRate = taxRate,
            TaxCollectedBy = collectedBy,
            ShipTo = new OrderShipTo { Name = "Dock", Line1 = "1 Main St", City = "Town", State = state, PostalCode = "00000" },
        };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = $"INV-{Guid.NewGuid():N}"[..20],
            CustomerId = customer.Id,
            SalesOrderId = order.Id,
            CurrencyId = currencyId,
            FxRate = fxRate,
            Status = InvoiceStatus.Sent,
            TaxRate = taxRate,
            InvoiceDate = January,
            DueDate = January.AddDays(30),
        };
        var number = 1;
        foreach (var (qty, price) in lines)
            invoice.Lines.Add(new InvoiceLine { Description = "Item", Quantity = qty, UnitPrice = price, LineNumber = number++ });
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        await Posting().PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 1);
        return invoice;
    }

    private async Task<decimal> TaxPayableNetCreditsAsync()
    {
        var lines = await _db.JournalLines.AsNoTracking().Where(l => l.GlAccountId == TaxPayableId).ToListAsync();
        return lines.Sum(l => l.Credit > 0m ? l.FunctionalAmount : -l.FunctionalAmount);
    }

    [Fact]
    public async Task Total_equals_the_sales_tax_payable_credits_for_the_period()
    {
        await SeedBookAsync();
        await PostInvoiceAsync("UT", 0.25m, [(1m, 2.125m), (1m, 2.125m), (1m, 2.125m), (1m, 2.125m)]);
        await PostInvoiceAsync("AB", 0.0725m, [(1m, 10200m)], currencyId: CadId, fxRate: 0.73m);
        var voided = await PostInvoiceAsync("UT", 0.0725m, [(3m, 33.335m)]);
        await Posting().ReverseInvoiceFinalizedAsync(voided.Id, new DateOnly(2026, 1, 20), voidedByUserId: 1);
        voided.Status = InvoiceStatus.Voided;
        await _db.SaveChangesAsync();

        var report = await Handler().Handle(
            new GetSalesTaxLiabilityQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)), default);

        var gl = await TaxPayableNetCreditsAsync();
        gl.Should().Be(2.13m + 539.84m);
        report.TotalLiability.Should().Be(gl);
        report.Jurisdictions.Single(j => j.Jurisdiction == "UT").Liability.Should().Be(2.13m);
        report.Jurisdictions.Single(j => j.Jurisdiction == "UT").TaxableBase.Should().Be(8.52m);
        report.Jurisdictions.Single(j => j.Jurisdiction == "UT").InvoiceCount.Should().Be(1);
        report.Jurisdictions.Single(j => j.Jurisdiction == "AB").Liability.Should().Be(539.84m);
        report.Jurisdictions.Single(j => j.Jurisdiction == "AB").TaxableBase.Should().Be(7446m);
    }

    [Fact]
    public async Task Marketplace_collected_invoice_is_counted_but_contributes_no_liability()
    {
        await SeedBookAsync();
        await PostInvoiceAsync("CA", 0.10m, [(1m, 100m)], collectedBy: TaxCollectedBy.Marketplace);
        await PostInvoiceAsync("CA", 0.10m, [(1m, 100m)]);

        var report = await Handler().Handle(new GetSalesTaxLiabilityQuery(), default);

        var ca = report.Jurisdictions.Single(j => j.Jurisdiction == "CA");
        ca.Liability.Should().Be(10m);
        ca.TaxableBase.Should().Be(100m);
        ca.InvoiceCount.Should().Be(2);
    }

    [Fact]
    public async Task Entries_outside_the_window_are_left_out()
    {
        await SeedBookAsync();
        await PostInvoiceAsync("UT", 0.10m, [(1m, 100m)]);

        var report = await Handler().Handle(
            new GetSalesTaxLiabilityQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)), default);

        report.Jurisdictions.Should().BeEmpty();
        report.TotalLiability.Should().Be(0m);
    }

    public void Dispose() => _db.Dispose();
}
