using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

using static Forge.Tests.Helpers.InventoryLedgerTestBook;

namespace Forge.Tests.Handlers.Inventory;

public class UpdateCycleCountHandlerTests
{
    private static async Task<(AppDbContext Db, CycleCount Count, int BinContentId)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        await InventoryLedgerTestBook.SeedAsync(db);
        var part = new Part { PartNumber = "ORING-018", Description = "O-ring", InventoryClass = InventoryClass.Raw, ManualCostOverride = 0.09m };
        var bin = new StorageLocation { Name = "A1", LocationType = LocationType.Bin, IsActive = true };
        db.Parts.Add(part);
        db.StorageLocations.Add(bin);
        await db.SaveChangesAsync();
        var content = new BinContent { LocationId = bin.Id, EntityType = "part", EntityId = part.Id, Quantity = 2000m, PlacedAt = DateTimeOffset.UtcNow };
        db.BinContents.Add(content);
        db.Set<InventoryValuation>().Add(new InventoryValuation { BookId = BookId, PartId = part.Id, OnHandQuantity = 2000m, AverageUnitCost = 0.09m, TotalValue = 180m });
        await db.SaveChangesAsync();
        var count = new CycleCount
        {
            LocationId = bin.Id, CountedById = 7, CountedAt = DateTimeOffset.UtcNow,
            Lines = [new CycleCountLine { BinContentId = content.Id, EntityType = "part", EntityId = part.Id, ExpectedQuantity = 2000, ActualQuantity = 2000 }],
        };
        db.CycleCounts.Add(count);
        await db.SaveChangesAsync();
        return (db, count, content.Id);
    }

    private static UpdateCycleCountHandler Handler(AppDbContext db, bool fullGlOn = true)
        => new(new InventoryRepository(db), UserAccessor(7), new SystemClock(), db, AdjustmentPosting(db, fullGlOn));

    [Fact]
    public async Task Approve_WithShortage_SetsBinAndPostsOneAdjustmentJournalPerLine()
    {
        var (db, count, binContentId) = await SeedAsync();
        using var _ = db;
        var line = count.Lines.Single();

        await Handler(db).Handle(new UpdateCycleCountCommand(count.Id,
            new UpdateCycleCountRequestModel("Approved", null, [new UpdateCycleCountLineModel(line.Id, 1998, null)])), CancellationToken.None);

        (await db.BinContents.SingleAsync(b => b.Id == binContentId)).Quantity.Should().Be(1998m);
        db.BinMovements.Should().ContainSingle(m => m.Reason == BinMovementReason.CycleCount && m.Quantity == 2m);
        var lines = await LinesAsync(db, $"Inventory:CycleCountLine:{line.Id}:ADJUST");
        lines.Single(l => l.GlAccountId == WritedownId).Debit.Should().Be(0.18m);
        lines.Single(l => l.GlAccountId == RawId).Credit.Should().Be(0.18m);
        (await db.InventoryValuations.SingleAsync()).OnHandQuantity.Should().Be(1998m);
    }

    [Fact]
    public async Task Approve_WithFullGlOff_AdjustsBinOnly()
    {
        var (db, count, binContentId) = await SeedAsync();
        using var _ = db;
        var line = count.Lines.Single();

        await Handler(db, fullGlOn: false).Handle(new UpdateCycleCountCommand(count.Id,
            new UpdateCycleCountRequestModel("Approved", null, [new UpdateCycleCountLineModel(line.Id, 1998, null)])), CancellationToken.None);

        (await db.BinContents.SingleAsync(b => b.Id == binContentId)).Quantity.Should().Be(1998m);
        db.JournalEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveWithoutApproving_PostsNothing()
    {
        var (db, count, binContentId) = await SeedAsync();
        using var _ = db;
        var line = count.Lines.Single();

        await Handler(db).Handle(new UpdateCycleCountCommand(count.Id,
            new UpdateCycleCountRequestModel(null, null, [new UpdateCycleCountLineModel(line.Id, 1998, null)])), CancellationToken.None);

        (await db.BinContents.SingleAsync(b => b.Id == binContentId)).Quantity.Should().Be(2000m);
        db.JournalEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Approve_ABinThatMovedSinceTheCount_IsRefusedAndChangesNothing()
    {
        var (db, count, binContentId) = await SeedAsync();
        using var _ = db;
        var line = count.Lines.Single();
        (await db.BinContents.SingleAsync(b => b.Id == binContentId)).Quantity = 1990m;
        await db.SaveChangesAsync();

        var act = () => Handler(db).Handle(new UpdateCycleCountCommand(count.Id,
            new UpdateCycleCountRequestModel("Approved", null, [new UpdateCycleCountLineModel(line.Id, 1998, null)])), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*changed from 2000 to 1990*");
        db.ChangeTracker.Clear();
        (await db.BinContents.SingleAsync(b => b.Id == binContentId)).Quantity.Should().Be(1990m);
        db.BinMovements.Should().BeEmpty();
        db.JournalEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Approve_AnUntouchedLineOnAFractionalBin_KeepsTheFraction()
    {
        var db = TestDbContextFactory.Create();
        using var _ = db;
        await InventoryLedgerTestBook.SeedAsync(db);
        var part = new Part { PartNumber = "WIRE-2", Description = "Wire", InventoryClass = InventoryClass.Raw, ManualCostOverride = 1m };
        var bin = new StorageLocation { Name = "W1", LocationType = LocationType.Bin, IsActive = true };
        db.Parts.Add(part);
        db.StorageLocations.Add(bin);
        await db.SaveChangesAsync();
        var content = new BinContent { LocationId = bin.Id, EntityType = "part", EntityId = part.Id, Quantity = 12.75m, PlacedAt = DateTimeOffset.UtcNow };
        db.BinContents.Add(content);
        await db.SaveChangesAsync();
        var created = await new CreateCycleCountHandler(new InventoryRepository(db), UserAccessor(7))
            .Handle(new CreateCycleCountCommand(new CreateCycleCountRequestModel(bin.Id, null)), CancellationToken.None);

        created.Lines.Single().ExpectedQuantity.Should().Be(12.75m);
        await Handler(db).Handle(new UpdateCycleCountCommand(created.Id,
            new UpdateCycleCountRequestModel("Approved", null, null)), CancellationToken.None);

        (await db.BinContents.SingleAsync(b => b.Id == content.Id)).Quantity.Should().Be(12.75m);
        db.BinMovements.Should().BeEmpty();
    }

    [Fact]
    public async Task Approve_AFractionalCount_MovesAndPostsTheFraction()
    {
        var (db, count, binContentId) = await SeedAsync();
        using var _ = db;
        var line = count.Lines.Single();

        await Handler(db).Handle(new UpdateCycleCountCommand(count.Id,
            new UpdateCycleCountRequestModel("Approved", null, [new UpdateCycleCountLineModel(line.Id, 1997.5m, null)])), CancellationToken.None);

        (await db.BinContents.SingleAsync(b => b.Id == binContentId)).Quantity.Should().Be(1997.5m);
        db.BinMovements.Should().ContainSingle(m => m.Quantity == 2.5m && m.FromLocationId != null);
        (await LinesAsync(db, $"Inventory:CycleCountLine:{line.Id}:ADJUST")).Should().HaveCount(2);
        (await db.InventoryValuations.SingleAsync()).OnHandQuantity.Should().Be(1997.5m);
    }
}
