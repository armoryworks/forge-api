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
/// Phase-2 STAGE C — PO-receipt inventory / GRNI posting (ACCOUNTING_SUITE_PLAN §7 "PO receipt").
/// Proves: DARK by default; Dr INVENTORY_{RAW|WIP|FG} (per part class) / Cr GRNI (base) / Cr
/// FREIGHT_CLEARING (allocated freight) at landed PO cost; consumables/tools expense; multi-line receipts
/// aggregate; idempotent; no receipt number → no-op.
/// </summary>
public class Phase2ReceiptPostingServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int InvRawId = 130;
    private const int InvWipId = 131;
    private const int InvFgId = 132;
    private const int GrniId = 210;
    private const int FreightId = 220;
    private const int OpExId = 600;

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

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static ReceiptInventoryPostingService Service(AppDbContext db, bool fullGlOn)
        => new(db, Engine(db), new FakeCapabilities(fullGlOn));

    private static readonly DateOnly EntryDate = new(2026, 1, 15);

    private static async Task<AppDbContext> SeedAsync()
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
            new GlAccount { Id = InvRawId, BookId = BookId, AccountNumber = "13100", Name = "Inventory — Raw", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true },
            new GlAccount { Id = InvWipId, BookId = BookId, AccountNumber = "13200", Name = "Inventory — WIP", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true },
            new GlAccount { Id = InvFgId, BookId = BookId, AccountNumber = "13300", Name = "Inventory — FG", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true },
            new GlAccount { Id = GrniId, BookId = BookId, AccountNumber = "21000", Name = "GRNI", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FreightId, BookId = BookId, AccountNumber = "22000", Name = "Freight Clearing", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = OpExId, BookId = BookId, AccountNumber = "60000", Name = "G&A", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_RAW", GlAccountId = InvRawId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_WIP", GlAccountId = InvWipId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_FG", GlAccountId = InvFgId },
            new AccountDeterminationRule { BookId = BookId, Key = "GRNI", GlAccountId = GrniId },
            new AccountDeterminationRule { BookId = BookId, Key = "FREIGHT_CLEARING", GlAccountId = FreightId },
            new AccountDeterminationRule { BookId = BookId, Key = "OPERATING_EXPENSE", GlAccountId = OpExId });

        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>Adds a Part + PO line + a receiving record on the given receipt; returns the PO id. Pass an
    /// existing <paramref name="purchaseOrderId"/> to add another line to the SAME PO/receipt.</summary>
    private static async Task<int> AddReceiptLineAsync(
        AppDbContext db, string receiptNumber, InventoryClass cls, decimal qty, decimal unitPrice, decimal? freight,
        int? purchaseOrderId = null)
    {
        var part = new Part
        {
            PartNumber = $"P-{cls}-{Guid.NewGuid():N}", Description = "x",
            InventoryClass = cls, ProcurementSource = ProcurementSource.Buy,
        };
        db.Set<Part>().Add(part);

        int poId;
        if (purchaseOrderId is int existing)
        {
            poId = existing;
        }
        else
        {
            var po = new PurchaseOrder { PONumber = "PO-1", VendorId = 1, Status = PurchaseOrderStatus.Submitted };
            db.Set<PurchaseOrder>().Add(po);
            await db.SaveChangesAsync();
            poId = po.Id;
        }

        var line = new PurchaseOrderLine
        {
            PurchaseOrderId = poId, PartId = part.Id, OrderedQuantity = qty, UnitPrice = unitPrice,
        };
        db.Set<PurchaseOrderLine>().Add(line);
        await db.SaveChangesAsync();

        db.Set<ReceivingRecord>().Add(new ReceivingRecord
        {
            PurchaseOrderLineId = line.Id, QuantityReceived = qty, ReceiptNumber = receiptNumber, AllocatedFreight = freight,
        });
        await db.SaveChangesAsync();
        return poId;
    }

    [Fact]
    public async Task Receipt_WhenFullGlOff_IsNoOp()
    {
        using var db = await SeedAsync();
        var poId = await AddReceiptLineAsync(db, "R-1", InventoryClass.Raw, 10m, 5m, null);

        await Service(db, fullGlOn: false).PostReceiptAsync(poId, "R-1", EntryDate, receivedByUserId: 7);

        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Receipt_RawPart_PostsInventoryRawAndGrni()
    {
        using var db = await SeedAsync();
        var poId = await AddReceiptLineAsync(db, "R-1", InventoryClass.Raw, 10m, 5m, null);

        await Service(db, fullGlOn: true).PostReceiptAsync(poId, "R-1", EntryDate, receivedByUserId: 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Source.Should().Be(JournalSource.Inventory);
        entry.SourceType.Should().Be("Receipt");
        entry.Lines.Single(l => l.GlAccountId == InvRawId).Debit.Should().Be(50m);
        entry.Lines.Single(l => l.GlAccountId == GrniId).Credit.Should().Be(50m);
        entry.Lines.Should().NotContain(l => l.GlAccountId == FreightId);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Receipt_WithFreight_AddsFreightClearing()
    {
        using var db = await SeedAsync();
        var poId = await AddReceiptLineAsync(db, "R-1", InventoryClass.Raw, 10m, 5m, freight: 8m);

        await Service(db, fullGlOn: true).PostReceiptAsync(poId, "R-1", EntryDate, receivedByUserId: 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        // Landed: Dr Inventory 58 (50 + 8) / Cr GRNI 50 / Cr Freight 8.
        entry.Lines.Single(l => l.GlAccountId == InvRawId).Debit.Should().Be(58m);
        entry.Lines.Single(l => l.GlAccountId == GrniId).Credit.Should().Be(50m);
        entry.Lines.Single(l => l.GlAccountId == FreightId).Credit.Should().Be(8m);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Theory]
    [InlineData(InventoryClass.Subassembly, InvWipId)]
    [InlineData(InventoryClass.FinishedGood, InvFgId)]
    [InlineData(InventoryClass.Component, InvRawId)]
    [InlineData(InventoryClass.Consumable, OpExId)]
    [InlineData(InventoryClass.Tool, OpExId)]
    public async Task Receipt_PartClass_RoutesToExpectedDebitAccount(InventoryClass cls, int expectedDebitAccountId)
    {
        using var db = await SeedAsync();
        var poId = await AddReceiptLineAsync(db, "R-1", cls, 4m, 25m, null);

        await Service(db, fullGlOn: true).PostReceiptAsync(poId, "R-1", EntryDate, receivedByUserId: 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == expectedDebitAccountId).Debit.Should().Be(100m);
        entry.Lines.Single(l => l.GlAccountId == GrniId).Credit.Should().Be(100m);
    }

    [Fact]
    public async Task Receipt_MultipleLines_AggregatesGrni()
    {
        using var db = await SeedAsync();
        // Both lines share ONE PO + receipt (a real multi-line receipt).
        var poId = await AddReceiptLineAsync(db, "R-9", InventoryClass.Raw, 10m, 5m, null);             // 50 → INVENTORY_RAW
        await AddReceiptLineAsync(db, "R-9", InventoryClass.FinishedGood, 2m, 100m, null, poId);        // 200 → INVENTORY_FG

        await Service(db, fullGlOn: true).PostReceiptAsync(poId, "R-9", EntryDate, receivedByUserId: 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == InvRawId).Debit.Should().Be(50m);
        entry.Lines.Single(l => l.GlAccountId == InvFgId).Debit.Should().Be(200m);
        entry.Lines.Single(l => l.GlAccountId == GrniId).Credit.Should().Be(250m);
    }

    [Fact]
    public async Task Receipt_NoReceiptNumber_IsNoOp()
    {
        using var db = await SeedAsync();

        await Service(db, fullGlOn: true).PostReceiptAsync(1, receiptNumber: null, EntryDate, receivedByUserId: 7);

        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Receipt_CalledTwice_IsIdempotent()
    {
        using var db = await SeedAsync();
        var poId = await AddReceiptLineAsync(db, "R-1", InventoryClass.Raw, 10m, 5m, null);
        var service = Service(db, fullGlOn: true);

        await service.PostReceiptAsync(poId, "R-1", EntryDate, receivedByUserId: 7);
        await service.PostReceiptAsync(poId, "R-1", EntryDate, receivedByUserId: 7);

        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }
}
