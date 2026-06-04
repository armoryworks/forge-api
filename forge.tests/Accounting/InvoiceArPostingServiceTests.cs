using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Phase-1 STAGE A — AR / revenue / tax posting wired into the invoice finalize
/// flow (ACCOUNTING_SUITE_PLAN §7 matrix row 1–2, §8.4). Proves:
///   • DARK by default — a no-op while CAP-ACCT-FULLGL is OFF (zero behavior change);
///   • when FULLGL is ON and control has transferred → Dr AR / Cr SALES_REVENUE / Cr SALES_TAX_PAYABLE;
///   • when the invoice precedes delivery (PointInTime) → Cr DEFERRED_REVENUE instead;
///   • tax-exempt customers suppress the SALES_TAX_PAYABLE line;
///   • idempotent — a re-finalize returns the existing entry (no duplicate);
///   • COGS is NOT posted (deferred to Phase 2).
/// </summary>
public class InvoiceArPostingServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;

    private const int CashId = 100;
    private const int RevenueId = 101;
    private const int ArControlId = 102;
    private const int DeferredRevenueId = 103;
    private const int SalesTaxPayableId = 104;

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

    private static InvoiceArPostingService CreateService(AppDbContext db, bool fullGlOn)
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
            new GlAccount { Id = RevenueId, BookId = BookId, AccountNumber = "40000", Name = "Sales Revenue", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ArControlId, BookId = BookId, AccountNumber = "11000", Name = "Accounts Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true },
            new GlAccount { Id = DeferredRevenueId, BookId = BookId, AccountNumber = "24000", Name = "Deferred Revenue", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = SalesTaxPayableId, BookId = BookId, AccountNumber = "23000", Name = "Sales Tax Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });

        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = ArControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = RevenueId },
            new AccountDeterminationRule { BookId = BookId, Key = "DEFERRED_REVENUE", GlAccountId = DeferredRevenueId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_TAX_PAYABLE", GlAccountId = SalesTaxPayableId });

        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<Invoice> AddInvoiceAsync(
        AppDbContext db,
        decimal taxRate = 0.08m,
        bool taxExempt = false,
        int? shipmentId = null,
        Shipment? shipment = null)
    {
        var customer = new Customer { Name = "Acme Corp", IsTaxExempt = taxExempt };
        db.Set<Customer>().Add(customer);
        if (shipment is not null)
            db.Set<Shipment>().Add(shipment);
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = "INV-1001",
            CustomerId = customer.Id,
            ShipmentId = shipmentId,
            InvoiceDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 14, 0, 0, 0, TimeSpan.Zero),
            Status = InvoiceStatus.Draft,
            TaxRate = taxRate,
            Lines =
            [
                new InvoiceLine { Description = "Widget A", Quantity = 2, UnitPrice = 50m, LineNumber = 1 },
                new InvoiceLine { Description = "Widget B", Quantity = 1, UnitPrice = 100m, LineNumber = 2 },
            ],
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public async Task Post_WhenFullGlOff_IsNoOp()
    {
        using var db = await SeedAsync();
        var invoice = await AddInvoiceAsync(db);
        var service = CreateService(db, fullGlOn: false);

        await service.PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);

        // Dark by default: nothing posted, no ledger movement at all.
        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        (await db.LedgerBalances.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Post_WhenFullGlOn_Delivered_PostsArRevenueAndTax()
    {
        using var db = await SeedAsync();
        // Delivered shipment → control transferred → straight to revenue.
        var shipment = new Shipment
        {
            Id = 500, ShipmentNumber = "SHP-1", SalesOrderId = 1,
            Status = ShipmentStatus.Delivered,
            DeliveredDate = new DateTimeOffset(2026, 1, 14, 0, 0, 0, TimeSpan.Zero),
        };
        var invoice = await AddInvoiceAsync(db, taxRate: 0.08m, shipmentId: 500, shipment: shipment);
        var service = CreateService(db, fullGlOn: true);

        await service.PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Source.Should().Be(JournalSource.AR);
        entry.SourceType.Should().Be("Invoice");
        entry.SourceId.Should().Be(invoice.Id);
        entry.EntryDate.Should().Be(new DateOnly(2026, 1, 15));
        entry.Status.Should().Be(JournalEntryStatus.Posted);
        entry.PostedBy.Should().Be(7);

        // Subtotal = 2*50 + 1*100 = 200; tax = 200 * 0.08 = 16; AR = 216.
        var ar = entry.Lines.Single(l => l.GlAccountId == ArControlId);
        ar.Debit.Should().Be(216m);
        ar.SubledgerPartyType.Should().Be(SubledgerPartyType.Customer);
        ar.SubledgerPartyId.Should().Be(invoice.CustomerId);

        // Revenue is credited per line (two lines) → 100 + 100 = 200.
        entry.Lines.Where(l => l.GlAccountId == RevenueId).Sum(l => l.Credit).Should().Be(200m);
        // Tax accrues to SALES_TAX_PAYABLE.
        entry.Lines.Single(l => l.GlAccountId == SalesTaxPayableId).Credit.Should().Be(16m);
        // Deferred revenue is NOT used when delivered.
        entry.Lines.Should().NotContain(l => l.GlAccountId == DeferredRevenueId);

        // COGS deferred to Phase 2: no COGS / FG relief leg posted.
        entry.Lines.Should().OnlyContain(l =>
            l.GlAccountId == ArControlId || l.GlAccountId == RevenueId || l.GlAccountId == SalesTaxPayableId);

        // Balanced: Σ Dr == Σ Cr.
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Post_WhenFullGlOn_InvoiceBeforeDelivery_BooksDeferredRevenue()
    {
        using var db = await SeedAsync();
        // Shipment exists but is NOT delivered → invoice precedes control transfer.
        var shipment = new Shipment
        {
            Id = 501, ShipmentNumber = "SHP-2", SalesOrderId = 1,
            Status = ShipmentStatus.Packed, DeliveredDate = null,
        };
        var invoice = await AddInvoiceAsync(db, taxRate: 0.08m, shipmentId: 501, shipment: shipment);
        var service = CreateService(db, fullGlOn: true);

        await service.PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();

        // PointInTime + not yet delivered → revenue parks in DEFERRED_REVENUE.
        entry.Lines.Where(l => l.GlAccountId == DeferredRevenueId).Sum(l => l.Credit).Should().Be(200m);
        entry.Lines.Should().NotContain(l => l.GlAccountId == RevenueId);
        // Tax still accrues.
        entry.Lines.Single(l => l.GlAccountId == SalesTaxPayableId).Credit.Should().Be(16m);
        // AR for the full total.
        entry.Lines.Single(l => l.GlAccountId == ArControlId).Debit.Should().Be(216m);
    }

    [Fact]
    public async Task Post_WhenFullGlOn_NoShipment_RecognizesRevenueImmediately()
    {
        using var db = await SeedAsync();
        // No shipment link (service / job invoice) → finalize is the control event.
        var invoice = await AddInvoiceAsync(db, taxRate: 0m, shipmentId: null);
        var service = CreateService(db, fullGlOn: true);

        await service.PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Where(l => l.GlAccountId == RevenueId).Sum(l => l.Credit).Should().Be(200m);
        entry.Lines.Should().NotContain(l => l.GlAccountId == DeferredRevenueId);
        // No tax line at a 0 rate.
        entry.Lines.Should().NotContain(l => l.GlAccountId == SalesTaxPayableId);
        entry.Lines.Single(l => l.GlAccountId == ArControlId).Debit.Should().Be(200m);
    }

    [Fact]
    public async Task Post_TaxExemptCustomer_SuppressesTaxLine()
    {
        using var db = await SeedAsync();
        var invoice = await AddInvoiceAsync(db, taxRate: 0.08m, taxExempt: true);
        var service = CreateService(db, fullGlOn: true);

        await service.PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Should().NotContain(l => l.GlAccountId == SalesTaxPayableId);
        // AR = subtotal only (no tax) = 200.
        entry.Lines.Single(l => l.GlAccountId == ArControlId).Debit.Should().Be(200m);
    }

    [Fact]
    public async Task Post_CalledTwice_IsIdempotent()
    {
        using var db = await SeedAsync();
        var invoice = await AddInvoiceAsync(db, taxRate: 0m);
        var service = CreateService(db, fullGlOn: true);

        await service.PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);
        await service.PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);

        // A re-finalize returns the existing entry — no duplicate journal.
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }
}
