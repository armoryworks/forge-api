using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
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
/// Deferred-revenue reclass on delivery (§8.4 / matrix row 2 — the last Phase-2 trigger).
/// An invoice finalized BEFORE delivery books Cr DEFERRED_REVENUE and defers COGS; when its
/// shipment is delivered, <see cref="IInvoiceArPostingService.PostDeliveryReclassAsync"/>
/// posts Dr DEFERRED_REVENUE / Cr SALES_REVENUE per original line plus the deferred COGS /
/// finished-goods relief. Proves: dark by default; correct amounts; no-ops for
/// straight-to-revenue and reversed originals; idempotency; and the
/// ShipmentDelivered reaction fans out per pinned invoice.
/// </summary>
public class Phase2DeliveryReclassTests
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

    private const int OpenPeriodId = 1000;
    private const int ShipmentId = 600;

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
            new GlAccount { Id = RevenueId, BookId = BookId, AccountNumber = "40000", Name = "Sales Revenue", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ArControlId, BookId = BookId, AccountNumber = "11000", Name = "Accounts Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true },
            new GlAccount { Id = DeferredRevenueId, BookId = BookId, AccountNumber = "24000", Name = "Deferred Revenue", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = SalesTaxPayableId, BookId = BookId, AccountNumber = "23000", Name = "Sales Tax Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = CogsId, BookId = BookId, AccountNumber = "50000", Name = "Cost of Goods Sold", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = InventoryFgId, BookId = BookId, AccountNumber = "13300", Name = "Inventory — Finished Goods", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true });

        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = ArControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = RevenueId },
            new AccountDeterminationRule { BookId = BookId, Key = "DEFERRED_REVENUE", GlAccountId = DeferredRevenueId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_TAX_PAYABLE", GlAccountId = SalesTaxPayableId },
            new AccountDeterminationRule { BookId = BookId, Key = "COGS", GlAccountId = CogsId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_FG", GlAccountId = InventoryFgId });

        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>
    /// An invoice pinned to a NOT-yet-delivered shipment (control not transferred), with one
    /// finished-goods line (2 × 50, std cost 30) and one part-less line (1 × 100) — so the
    /// finalize books 200 to DEFERRED_REVENUE and defers the FG COGS.
    /// </summary>
    private static async Task<(Invoice Invoice, Shipment Shipment)> AddDeferredInvoiceAsync(AppDbContext db)
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

        var shipment = new Shipment
        {
            Id = ShipmentId, ShipmentNumber = "SHP-9", SalesOrderId = 1,
            Status = ShipmentStatus.InTransit,
        };
        db.Set<Shipment>().Add(shipment);
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = "INV-3001",
            CustomerId = customer.Id,
            ShipmentId = ShipmentId,
            InvoiceDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 14, 0, 0, 0, TimeSpan.Zero),
            Status = InvoiceStatus.Draft,
            TaxRate = 0m,
            Lines =
            [
                new InvoiceLine { PartId = part.Id, Description = "Widget", Quantity = 2, UnitPrice = 50m, LineNumber = 1 },
                new InvoiceLine { Description = "Setup service", Quantity = 1, UnitPrice = 100m, LineNumber = 2 },
            ],
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return (invoice, shipment);
    }

    private static async Task DeliverAsync(AppDbContext db, Shipment shipment)
    {
        shipment.Status = ShipmentStatus.Delivered;
        shipment.DeliveredDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Reclass_WhenFullGlOff_IsNoOp()
    {
        using var db = await SeedAsync();
        var (invoice, shipment) = await AddDeferredInvoiceAsync(db);
        await DeliverAsync(db, shipment);

        await CreateService(db, fullGlOn: false).PostDeliveryReclassAsync(invoice.Id, deliveredByUserId: 7);

        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Reclass_DeferredInvoice_MovesDeferredToRevenue_AndPostsCogs()
    {
        using var db = await SeedAsync();
        var (invoice, shipment) = await AddDeferredInvoiceAsync(db);
        var service = CreateService(db, fullGlOn: true);

        await service.PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);
        // Finalize before delivery: deferred, no COGS yet.
        var original = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        original.Lines.Where(l => l.GlAccountId == DeferredRevenueId).Sum(l => l.Credit).Should().Be(200m);

        await DeliverAsync(db, shipment);
        await service.PostDeliveryReclassAsync(invoice.Id, deliveredByUserId: 8);

        var entries = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).ToListAsync();
        entries.Should().HaveCount(3); // original AR + reclass + COGS

        var reclass = entries.Single(e => e.IdempotencyKey == $"{JournalSource.AR}:Invoice:{invoice.Id}:REVENUE_RECLASS");
        reclass.EntryDate.Should().Be(new DateOnly(2026, 1, 20), "recognition happens on the delivery date");
        reclass.PostedBy.Should().Be(8);
        reclass.Lines.Where(l => l.GlAccountId == DeferredRevenueId).Sum(l => l.Debit).Should().Be(200m);
        reclass.Lines.Where(l => l.GlAccountId == RevenueId).Sum(l => l.Credit).Should().Be(200m);
        reclass.Lines.Sum(l => l.Debit).Should().Be(reclass.Lines.Sum(l => l.Credit));

        var cogs = entries.Single(e => e.Source == JournalSource.Inventory);
        cogs.Lines.Single(l => l.GlAccountId == CogsId).Debit.Should().Be(60m);          // 30 std × 2
        cogs.Lines.Single(l => l.GlAccountId == InventoryFgId).Credit.Should().Be(60m);
    }

    [Fact]
    public async Task Reclass_StraightRevenueInvoice_IsNoOp()
    {
        using var db = await SeedAsync();
        var (invoice, shipment) = await AddDeferredInvoiceAsync(db);
        // Delivered BEFORE finalize → straight to revenue, COGS at finalize.
        await DeliverAsync(db, shipment);
        var service = CreateService(db, fullGlOn: true);
        await service.PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);
        var countAfterFinalize = await db.JournalEntries.IgnoreQueryFilters().CountAsync();

        await service.PostDeliveryReclassAsync(invoice.Id, deliveredByUserId: 8);

        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(countAfterFinalize,
            "an invoice booked straight to revenue has nothing to reclass");
    }

    [Fact]
    public async Task Reclass_IsIdempotent()
    {
        using var db = await SeedAsync();
        var (invoice, shipment) = await AddDeferredInvoiceAsync(db);
        var service = CreateService(db, fullGlOn: true);
        await service.PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);
        await DeliverAsync(db, shipment);

        await service.PostDeliveryReclassAsync(invoice.Id, deliveredByUserId: 8);
        await service.PostDeliveryReclassAsync(invoice.Id, deliveredByUserId: 8);

        (await db.JournalEntries.IgnoreQueryFilters()
            .CountAsync(e => e.IdempotencyKey == $"{JournalSource.AR}:Invoice:{invoice.Id}:REVENUE_RECLASS"))
            .Should().Be(1);
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync(e => e.Source == JournalSource.Inventory))
            .Should().Be(1, "COGS de-dupes on its own :COGS key");
    }

    [Fact]
    public async Task Reclass_ReversedOriginal_IsNoOp()
    {
        using var db = await SeedAsync();
        var (invoice, shipment) = await AddDeferredInvoiceAsync(db);
        var service = CreateService(db, fullGlOn: true);
        await service.PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);

        // Void path: the original AR entry was reversed — delivery must not resurrect revenue.
        var original = await db.JournalEntries.IgnoreQueryFilters().SingleAsync();
        original.Status = JournalEntryStatus.Reversed;
        await db.SaveChangesAsync();
        await DeliverAsync(db, shipment);

        await service.PostDeliveryReclassAsync(invoice.Id, deliveredByUserId: 8);

        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1,
            "a reversed original posts neither a reclass nor COGS");
    }

    [Fact]
    public async Task Reaction_PostsForEachInvoicePinnedToTheShipment()
    {
        using var db = await SeedAsync();
        var (invoice, _) = await AddDeferredInvoiceAsync(db);
        var posting = new Mock<IInvoiceArPostingService>();
        var reaction = new OnShipmentDelivered_ReclassDeferredRevenue(db, posting.Object);

        await reaction.Handle(new ShipmentDeliveredEvent(ShipmentId, SalesOrderId: 1, UserId: 9), CancellationToken.None);

        posting.Verify(p => p.PostDeliveryReclassAsync(invoice.Id, 9, It.IsAny<CancellationToken>()), Times.Once);
        posting.VerifyNoOtherCalls();
    }
}
