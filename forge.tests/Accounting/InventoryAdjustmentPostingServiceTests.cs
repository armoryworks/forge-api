using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

using static Forge.Tests.Helpers.InventoryLedgerTestBook;

namespace Forge.Tests.Accounting;

public class InventoryAdjustmentPostingServiceTests
{
    private static readonly DateOnly EntryDate = new(2026, 3, 2);

    private static async Task<(AppDbContext Db, Part Part)> SeedAsync(
        InventoryClass inventoryClass = InventoryClass.Raw, decimal? storeQty = 2000m, decimal storeValue = 180m,
        decimal? standard = 0.09m, bool withWritedownRule = true)
    {
        var db = TestDbContextFactory.Create();
        await InventoryLedgerTestBook.SeedAsync(db, withWritedownRule);
        var part = new Part { PartNumber = "ORING-018", Description = "O-ring", InventoryClass = inventoryClass, ManualCostOverride = standard };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        if (storeQty is { } qty)
        {
            db.Set<InventoryValuation>().Add(new InventoryValuation
            {
                BookId = BookId, PartId = part.Id, OnHandQuantity = qty, TotalValue = storeValue,
                AverageUnitCost = qty == 0m ? 0m : Math.Round(storeValue / qty, 6),
            });
            await db.SaveChangesAsync();
        }
        return (db, part);
    }

    private static InventoryAdjustmentPosting Adjustment(int partId, decimal change, long sourceId = 1)
        => new("CycleCountLine", sourceId, partId, change, EntryDate, 7, "cycle count 1");

    [Fact]
    public async Task FullGlOff_PostsNothing()
    {
        var (db, part) = await SeedAsync();
        using var _ = db;

        await AdjustmentPosting(db, fullGlOn: false).PostAdjustmentAsync(Adjustment(part.Id, -2m));

        db.JournalEntries.Should().BeEmpty();
        (await db.InventoryValuations.SingleAsync()).OnHandQuantity.Should().Be(2000m);
    }

    [Fact]
    public async Task Shortage_RelievesStoreAtAverage_AndCreditsInventory()
    {
        var (db, part) = await SeedAsync();
        using var _ = db;

        await AdjustmentPosting(db).PostAdjustmentAsync(Adjustment(part.Id, -2m));

        var lines = await LinesAsync(db, "Inventory:CycleCountLine:1:ADJUST");
        lines.Single(l => l.GlAccountId == WritedownId).Debit.Should().Be(0.18m);
        lines.Single(l => l.GlAccountId == RawId).Credit.Should().Be(0.18m);
        var row = await db.InventoryValuations.SingleAsync();
        row.OnHandQuantity.Should().Be(1998m);
        row.TotalValue.Should().Be(179.82m);
    }

    [Fact]
    public async Task Overage_ReCreditsStoreAtAverage_AndDebitsInventory()
    {
        var (db, part) = await SeedAsync(InventoryClass.FinishedGood, storeQty: 10m, storeValue: 50m);
        using var _ = db;

        await AdjustmentPosting(db).PostAdjustmentAsync(Adjustment(part.Id, 3m));

        var lines = await LinesAsync(db, "Inventory:CycleCountLine:1:ADJUST");
        lines.Single(l => l.GlAccountId == FgId).Debit.Should().Be(15m);
        lines.Single(l => l.GlAccountId == WritedownId).Credit.Should().Be(15m);
        (await db.InventoryValuations.SingleAsync()).OnHandQuantity.Should().Be(13m);
    }

    [Fact]
    public async Task PartWithoutStoreRow_PostsAtStandard_AndLeavesStoreAlone()
    {
        var (db, part) = await SeedAsync(InventoryClass.Subassembly, storeQty: null, standard: 4m);
        using var _ = db;

        await AdjustmentPosting(db).PostAdjustmentAsync(Adjustment(part.Id, -5m));

        var lines = await LinesAsync(db, "Inventory:CycleCountLine:1:ADJUST");
        lines.Single(l => l.GlAccountId == SubassemblyId).Credit.Should().Be(20m);
        db.InventoryValuations.Should().BeEmpty();
    }

    [Fact]
    public async Task OverageWithoutStoreRow_PostsAtStandard_AndStartsTheStore()
    {
        var (db, part) = await SeedAsync(InventoryClass.Subassembly, storeQty: null, standard: 4m);
        using var _ = db;

        await AdjustmentPosting(db).PostAdjustmentAsync(Adjustment(part.Id, 5m));

        var lines = await LinesAsync(db, "Inventory:CycleCountLine:1:ADJUST");
        lines.Single(l => l.GlAccountId == SubassemblyId).Debit.Should().Be(20m);
        var row = await db.InventoryValuations.SingleAsync();
        (row.OnHandQuantity, row.TotalValue).Should().Be((5m, 20m));
    }

    [Fact]
    public async Task RePost_IsNoOp_AndDoesNotRelieveTheStoreTwice()
    {
        var (db, part) = await SeedAsync();
        using var _ = db;
        var service = AdjustmentPosting(db);

        await service.PostAdjustmentAsync(Adjustment(part.Id, -2m));
        await service.PostAdjustmentAsync(Adjustment(part.Id, -2m));

        db.JournalEntries.Should().ContainSingle();
        (await db.InventoryValuations.SingleAsync()).OnHandQuantity.Should().Be(1998m);
    }

    [Theory]
    [InlineData(InventoryClass.Consumable)]
    [InlineData(InventoryClass.Tool)]
    public async Task NonStockedPart_IsSkipped(InventoryClass inventoryClass)
    {
        var (db, part) = await SeedAsync(inventoryClass);
        using var _ = db;

        await AdjustmentPosting(db).PostAdjustmentAsync(Adjustment(part.Id, -2m));

        db.JournalEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task PartWithNoCost_IsSkipped()
    {
        var (db, part) = await SeedAsync(storeQty: null, standard: null);
        using var _ = db;

        await AdjustmentPosting(db).PostAdjustmentAsync(Adjustment(part.Id, -2m));

        db.JournalEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task BookWithoutWritedownRule_IsSkipped_WithoutTouchingTheStore()
    {
        var (db, part) = await SeedAsync(withWritedownRule: false);
        using var _ = db;

        await AdjustmentPosting(db).PostAdjustmentAsync(Adjustment(part.Id, -2m));

        db.JournalEntries.Should().BeEmpty();
        (await db.InventoryValuations.SingleAsync()).OnHandQuantity.Should().Be(2000m);
    }
}
