using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

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
/// Voiding a finalized invoice reverses everything it posted:
/// <see cref="IInvoiceArPostingService.ReverseInvoiceFinalizedAsync"/> reverses the AR / revenue / tax,
/// delivery-reclass and COGS entries, voids the AR open item and gives relieved finished goods back to
/// the valuation store, without ever creating a store row.
/// </summary>
public class InvoiceVoidReversalTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;

    private const int RevenueId = 101;
    private const int ArControlId = 102;
    private const int DeferredRevenueId = 103;
    private const int SalesTaxPayableId = 104;
    private const int CogsId = 105;
    private const int InventoryFgId = 106;
    private const int InventorySubassemblyId = 107;

    private const int JanPeriodId = 1000;
    private const int FebPeriodId = 1001;
    private const int ShipmentId = 600;

    private static readonly DateOnly VoidDate = new(2026, 2, 10);

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

    private static InvoiceArPostingService CreateService(
        AppDbContext db, bool fullGlOn = true, IStandardCostResolver? standardCost = null)
        => new(
            db,
            new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock()),
            new FakeCapabilities(fullGlOn),
            valuation: new InventoryValuationService(db),
            standardCost: standardCost);

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

        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = FiscalYearId, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().AddRange(
            new FiscalPeriod
            {
                Id = JanPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "Jan 2026",
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31),
                Status = FiscalPeriodStatus.Open,
            },
            new FiscalPeriod
            {
                Id = FebPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 2, Name = "Feb 2026",
                StartDate = new DateOnly(2026, 2, 1), EndDate = new DateOnly(2026, 2, 28),
                Status = FiscalPeriodStatus.Open,
            });

        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = RevenueId, BookId = BookId, AccountNumber = "40000", Name = "Sales Revenue", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ArControlId, BookId = BookId, AccountNumber = "11000", Name = "Accounts Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true },
            new GlAccount { Id = DeferredRevenueId, BookId = BookId, AccountNumber = "24000", Name = "Deferred Revenue", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = SalesTaxPayableId, BookId = BookId, AccountNumber = "23000", Name = "Sales Tax Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = CogsId, BookId = BookId, AccountNumber = "50000", Name = "Cost of Goods Sold", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = InventoryFgId, BookId = BookId, AccountNumber = "13300", Name = "Inventory — Finished Goods", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true },
            new GlAccount { Id = InventorySubassemblyId, BookId = BookId, AccountNumber = "13250", Name = "Inventory — Subassemblies", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true });

        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = ArControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = RevenueId },
            new AccountDeterminationRule { BookId = BookId, Key = "DEFERRED_REVENUE", GlAccountId = DeferredRevenueId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_TAX_PAYABLE", GlAccountId = SalesTaxPayableId },
            new AccountDeterminationRule { BookId = BookId, Key = "COGS", GlAccountId = CogsId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_FG", GlAccountId = InventoryFgId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_SUBASSEMBLY", GlAccountId = InventorySubassemblyId });

        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>
    /// One finished-goods line (2 × 50, std cost 30) and one part-less line (1 × 100) at 10% tax. With
    /// <paramref name="shipmentStatus"/> null the invoice has no shipment and books straight to revenue.
    /// </summary>
    private static async Task<(Invoice Invoice, Part Part, Shipment? Shipment)> AddInvoiceAsync(
        AppDbContext db, ShipmentStatus? shipmentStatus = null)
    {
        var customer = new Customer { Name = "Acme Corp" };
        db.Set<Customer>().Add(customer);

        var part = new Part
        {
            PartNumber = "FG-1",
            Description = "Widget",
            InventoryClass = InventoryClass.FinishedGood,
            ProcurementSource = ProcurementSource.Make,
            ManualCostOverride = 30m,
        };
        db.Set<Part>().Add(part);

        Shipment? shipment = null;
        if (shipmentStatus is { } status)
        {
            shipment = new Shipment { Id = ShipmentId, ShipmentNumber = "SHP-9", SalesOrderId = 1, Status = status };
            db.Set<Shipment>().Add(shipment);
        }
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = "INV-4001",
            CustomerId = customer.Id,
            ShipmentId = shipment?.Id,
            InvoiceDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 14, 0, 0, 0, TimeSpan.Zero),
            Status = InvoiceStatus.Sent,
            TaxRate = 0.10m,
            Lines =
            [
                new InvoiceLine { PartId = part.Id, Description = "Widget", Quantity = 2, UnitPrice = 50m, LineNumber = 1 },
                new InvoiceLine { Description = "Setup service", Quantity = 1, UnitPrice = 100m, LineNumber = 2 },
            ],
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return (invoice, part, shipment);
    }

    private static async Task AddStoreRowAsync(AppDbContext db, int partId, decimal qty, decimal avg)
    {
        db.InventoryValuations.Add(new InventoryValuation
        {
            BookId = BookId, PartId = partId, OnHandQuantity = qty, AverageUnitCost = avg, TotalValue = qty * avg,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Dictionary<int, decimal>> NetByAccountAsync(AppDbContext db)
        => (await db.JournalLines.IgnoreQueryFilters().ToListAsync())
            .GroupBy(l => l.GlAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Debit - l.Credit));

    [Fact]
    public async Task Reverse_WhenFullGlOff_IsNoOp()
    {
        using var db = await SeedAsync();
        var (invoice, _, _) = await AddInvoiceAsync(db);

        await CreateService(db, fullGlOn: false).ReverseInvoiceFinalizedAsync(invoice.Id, VoidDate, 7);

        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Reverse_StraightToRevenue_NetsEveryAccountToZero_AndRestoresTheStore()
    {
        using var db = await SeedAsync();
        var (invoice, part, _) = await AddInvoiceAsync(db);
        await AddStoreRowAsync(db, part.Id, qty: 10m, avg: 30m);
        var service = CreateService(db);
        await service.PostInvoiceFinalizedAsync(invoice.Id, 7);

        var store = await db.InventoryValuations.SingleAsync();
        store.OnHandQuantity.Should().Be(8m);
        store.TotalValue.Should().Be(240m);

        await service.ReverseInvoiceFinalizedAsync(invoice.Id, VoidDate, 9);

        var net = await NetByAccountAsync(db);
        net.Should().NotBeEmpty();
        net.Values.Should().AllSatisfy(v => v.Should().Be(0m));
        net.Keys.Should().Contain([ArControlId, RevenueId, SalesTaxPayableId, CogsId, InventoryFgId]);

        var entries = await db.JournalEntries.IgnoreQueryFilters().ToListAsync();
        entries.Where(e => e.SourceType == "Invoice").Should().AllSatisfy(e => e.Status.Should().Be(JournalEntryStatus.Reversed));
        entries.Where(e => e.ReversalOfEntryId != null).Should().HaveCount(2)
            .And.AllSatisfy(e =>
            {
                e.EntryDate.Should().Be(new DateOnly(2026, 1, 15), "an open original period reverses in that period");
                e.PostedBy.Should().Be(9);
            });

        (await db.ArOpenItems.IgnoreQueryFilters().SingleAsync()).Status.Should().Be(OpenItemStatus.Voided);

        store = await db.InventoryValuations.SingleAsync();
        store.OnHandQuantity.Should().Be(10m);
        store.TotalValue.Should().Be(300m);
    }

    [Fact]
    public async Task Reverse_SubassemblySale_NetsTheSubassemblyAccountToZero_AndRestoresTheStore()
    {
        using var db = await SeedAsync();
        var (invoice, part, _) = await AddInvoiceAsync(db);
        part.InventoryClass = InventoryClass.Subassembly;
        await db.SaveChangesAsync();
        await AddStoreRowAsync(db, part.Id, qty: 10m, avg: 30m);
        var service = CreateService(db);
        await service.PostInvoiceFinalizedAsync(invoice.Id, 7);

        (await NetByAccountAsync(db))[InventorySubassemblyId].Should().Be(-60m);
        (await db.InventoryValuations.SingleAsync()).OnHandQuantity.Should().Be(8m);

        await service.ReverseInvoiceFinalizedAsync(invoice.Id, VoidDate, 9);

        var net = await NetByAccountAsync(db);
        net.Values.Should().AllSatisfy(v => v.Should().Be(0m));
        net.Keys.Should().NotContain(InventoryFgId);
        var store = await db.InventoryValuations.SingleAsync();
        store.OnHandQuantity.Should().Be(10m);
        store.TotalValue.Should().Be(300m);
    }

    [Fact]
    public async Task Reverse_DeferredInvoiceNotDelivered_ReversesOnlyTheArEntry()
    {
        using var db = await SeedAsync();
        var (invoice, part, _) = await AddInvoiceAsync(db, ShipmentStatus.InTransit);
        await AddStoreRowAsync(db, part.Id, qty: 10m, avg: 30m);
        var service = CreateService(db);
        await service.PostInvoiceFinalizedAsync(invoice.Id, 7);

        await service.ReverseInvoiceFinalizedAsync(invoice.Id, VoidDate, 9);

        (await NetByAccountAsync(db)).Values.Should().AllSatisfy(v => v.Should().Be(0m));
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(2);
        (await db.InventoryValuations.SingleAsync()).OnHandQuantity.Should().Be(10m, "nothing was relieved, so nothing goes back");
    }

    [Fact]
    public async Task Reverse_DeferredInvoiceDelivered_ReversesArReclassAndCogs()
    {
        using var db = await SeedAsync();
        var (invoice, part, shipment) = await AddInvoiceAsync(db, ShipmentStatus.InTransit);
        await AddStoreRowAsync(db, part.Id, qty: 10m, avg: 30m);
        var service = CreateService(db);
        await service.PostInvoiceFinalizedAsync(invoice.Id, 7);
        shipment!.Status = ShipmentStatus.Delivered;
        shipment.DeliveredDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);
        await db.SaveChangesAsync();
        await service.PostDeliveryReclassAsync(invoice.Id, 8);

        await service.ReverseInvoiceFinalizedAsync(invoice.Id, VoidDate, 9);

        (await NetByAccountAsync(db)).Values.Should().AllSatisfy(v => v.Should().Be(0m));
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync(e => e.ReversalOfEntryId != null)).Should().Be(3);
        var store = await db.InventoryValuations.SingleAsync();
        store.OnHandQuantity.Should().Be(10m);
        store.TotalValue.Should().Be(300m);
    }

    [Fact]
    public async Task Reverse_StandardCostPath_RestoresTheStoreAtTheCogsValue()
    {
        using var db = await SeedAsync();
        var (invoice, part, _) = await AddInvoiceAsync(db);
        await AddStoreRowAsync(db, part.Id, qty: 10m, avg: 25m);
        var resolver = new Mock<IStandardCostResolver>();
        resolver.Setup(r => r.ResolveAsync(part.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StandardCostElements(30m, 0m, 0m));
        var service = CreateService(db, standardCost: resolver.Object);
        await service.PostInvoiceFinalizedAsync(invoice.Id, 7);

        var cogs = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .SingleAsync(e => e.Source == JournalSource.Inventory);
        cogs.Lines.Single(l => l.GlAccountId == CogsId).Debit.Should().Be(60m);
        (await db.InventoryValuations.SingleAsync()).TotalValue.Should().Be(200m);

        await service.ReverseInvoiceFinalizedAsync(invoice.Id, VoidDate, 9);

        var store = await db.InventoryValuations.SingleAsync();
        store.OnHandQuantity.Should().Be(10m);
        store.TotalValue.Should().Be(260m, "the store gets back the 60 the GL put back into finished goods");
        (await NetByAccountAsync(db)).Values.Should().AllSatisfy(v => v.Should().Be(0m));
    }

    [Fact]
    public async Task Reverse_NoStoreRow_NeverCreatesOne()
    {
        using var db = await SeedAsync();
        var (invoice, _, _) = await AddInvoiceAsync(db);
        var service = CreateService(db);
        await service.PostInvoiceFinalizedAsync(invoice.Id, 7);

        await service.ReverseInvoiceFinalizedAsync(invoice.Id, VoidDate, 9);

        (await db.InventoryValuations.AnyAsync()).Should().BeFalse();
        (await NetByAccountAsync(db)).Values.Should().AllSatisfy(v => v.Should().Be(0m));
    }

    [Fact]
    public async Task Reverse_Twice_DoesNothingTheSecondTime()
    {
        using var db = await SeedAsync();
        var (invoice, part, _) = await AddInvoiceAsync(db);
        await AddStoreRowAsync(db, part.Id, qty: 10m, avg: 30m);
        var service = CreateService(db);
        await service.PostInvoiceFinalizedAsync(invoice.Id, 7);
        await service.ReverseInvoiceFinalizedAsync(invoice.Id, VoidDate, 9);
        var count = await db.JournalEntries.IgnoreQueryFilters().CountAsync();

        await service.ReverseInvoiceFinalizedAsync(invoice.Id, VoidDate, 9);

        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(count);
        (await db.InventoryValuations.SingleAsync()).OnHandQuantity.Should().Be(10m);
    }

    [Fact]
    public async Task Reverse_OriginalPeriodHardClosed_ReversesOnTheVoidDate()
    {
        using var db = await SeedAsync();
        var (invoice, _, _) = await AddInvoiceAsync(db);
        var service = CreateService(db);
        await service.PostInvoiceFinalizedAsync(invoice.Id, 7);
        (await db.Set<FiscalPeriod>().SingleAsync(p => p.Id == JanPeriodId)).Status = FiscalPeriodStatus.HardClosed;
        await db.SaveChangesAsync();

        await service.ReverseInvoiceFinalizedAsync(invoice.Id, VoidDate, 9);

        var reversals = await db.JournalEntries.IgnoreQueryFilters().Where(e => e.ReversalOfEntryId != null).ToListAsync();
        reversals.Should().NotBeEmpty().And.AllSatisfy(e =>
        {
            e.EntryDate.Should().Be(VoidDate);
            e.FiscalPeriodId.Should().Be(FebPeriodId);
        });
        (await NetByAccountAsync(db)).Values.Should().AllSatisfy(v => v.Should().Be(0m));
    }

    [Fact]
    public async Task DeliveryAfterVoid_PostsNothing()
    {
        using var db = await SeedAsync();
        var (invoice, _, shipment) = await AddInvoiceAsync(db, ShipmentStatus.InTransit);
        var service = CreateService(db);
        await service.PostInvoiceFinalizedAsync(invoice.Id, 7);
        invoice.Status = InvoiceStatus.Voided;
        shipment!.Status = ShipmentStatus.Delivered;
        shipment.DeliveredDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);
        await db.SaveChangesAsync();

        await service.PostDeliveryReclassAsync(invoice.Id, 8);

        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1,
            "a voided invoice posts neither the reclass nor COGS, even before its AR entry is reversed");
    }
}
