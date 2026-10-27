using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Inventory.PhysicalInventories;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Interceptors;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

/// <summary>
/// The physical inventory against real Postgres: the freeze refuses stock movements in its scope and lets others
/// through, cancel releases it, and posting adjusts the bins, writes the movements, relieves or adds to the valuation
/// store and posts the write-down journal in one transaction.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PhysicalInventoryPostgresTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
    }

    private sealed record Seeded(int PartId, int OtherPartId, int ParentId, int ChildId, int OutsideId, int ChildBinId, int OutsideBinId);

    private static PhysicalInventoryService Service(AppDbContext db)
        => new(db, InventoryLedgerTestBook.AdjustmentPosting(db), new SystemClock());

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

    private async Task<Seeded> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        await InventoryLedgerTestBook.SeedAsync(db);

        var part = new Part { PartNumber = "PI-RESIN", Name = "Resin", InventoryClass = InventoryClass.Raw, ManualCostOverride = 10m };
        var other = new Part { PartNumber = "PI-PIN", Name = "Pin", InventoryClass = InventoryClass.Raw, ManualCostOverride = 2m };
        var parent = new StorageLocation { Name = "Warehouse", LocationType = LocationType.Area, IsActive = true };
        var outside = new StorageLocation { Name = "Receiving dock", LocationType = LocationType.Area, IsActive = true };
        db.AddRange(part, other, parent, outside);
        await db.SaveChangesAsync();
        var child = new StorageLocation { Name = "Rack 1", LocationType = LocationType.Bin, ParentId = parent.Id, IsActive = true };
        db.Add(child);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var childBin = new BinContent { LocationId = child.Id, EntityType = "part", EntityId = part.Id, Quantity = 100m, PlacedBy = 1, PlacedAt = now };
        var outsideBin = new BinContent { LocationId = outside.Id, EntityType = "part", EntityId = part.Id, Quantity = 50m, PlacedBy = 1, PlacedAt = now };
        db.AddRange(childBin, outsideBin);
        await db.SaveChangesAsync();
        await new InventoryValuationService(db).ApplyReceiptAsync(InventoryLedgerTestBook.BookId, part.Id, 150m, 1500m);
        await db.SaveChangesAsync();
        return new Seeded(part.Id, other.Id, parent.Id, child.Id, outside.Id, childBin.Id, outsideBin.Id);
    }

    private static async Task MoveOutAsync(AppDbContext db, int binId, decimal quantity)
    {
        var bin = await db.BinContents.SingleAsync(c => c.Id == binId);
        bin.Quantity -= quantity;
        db.BinMovements.Add(new BinMovement
        {
            EntityType = "part", EntityId = bin.EntityId, Quantity = quantity, FromLocationId = bin.LocationId,
            MovedBy = 1, MovedAt = DateTimeOffset.UtcNow, Reason = BinMovementReason.Issue,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<int> CreateAndFreezeAsync(AppDbContext db, int scopeId, decimal tolerance = 0m)
    {
        var service = Service(db);
        var created = await service.CreateAsync(new CreatePhysicalInventoryRequestModel
        {
            Description = "Year end", ScopeLocationId = scopeId, CountDate = new DateOnly(2026, 6, 30),
            BlindCount = true, RecountTolerancePercent = tolerance,
        }, 1);
        created.InventoryNumber.Should().StartWith("PI-");
        var frozen = await service.FreezeAsync(created.Id, 1);
        frozen.Status.Should().Be(PhysicalInventoryStatus.Frozen);
        return created.Id;
    }

    [Fact]
    public async Task Freeze_blocks_movements_in_scope_only_and_cancel_releases_them()
    {
        var s = await SeedAsync();
        int id;
        await using (var db = fixture.CreateContext())
        {
            id = await CreateAndFreezeAsync(db, s.ParentId);
            var detail = await Service(db).GetAsync(id);
            detail.Tags.Should().ContainSingle().Which.LocationId.Should().Be(s.ChildId);
            detail.Tags[0].FrozenQuantity.Should().BeNull("a blind count hides the book quantity while counting");
        }

        await using (var db = fixture.CreateContext())
        {
            var blocked = () => MoveOutAsync(db, s.ChildBinId, 5m);
            (await blocked.Should().ThrowAsync<PhysicalInventoryFrozenException>()).Which.InventoryNumber.Should().StartWith("PI-");
        }

        await using (var db = fixture.CreateContext())
        {
            await MoveOutAsync(db, s.OutsideBinId, 5m);
            (await db.BinContents.SingleAsync(c => c.Id == s.OutsideBinId)).Quantity.Should().Be(45m);
        }

        await using (var db = fixture.CreateContext())
        {
            (await Service(db).CancelAsync(id, 1)).Status.Should().Be(PhysicalInventoryStatus.Cancelled);
        }

        await using (var db = fixture.CreateContext())
        {
            await MoveOutAsync(db, s.ChildBinId, 5m);
            (await db.BinContents.SingleAsync(c => c.Id == s.ChildBinId)).Quantity.Should().Be(95m);
        }
    }

    [Fact]
    public async Task Post_adjusts_stock_valuation_and_the_ledger_in_one_go()
    {
        var s = await SeedAsync();
        int id;
        await using (var db = fixture.CreateContext())
        {
            id = await CreateAndFreezeAsync(db, s.ParentId);
            var service = Service(db);
            var tag = (await service.GetAsync(id)).Tags.Single();
            await service.EnterCountAsync(id, tag.Id, new EnterPhysicalCountRequestModel { Quantity = 90m }, 2);
            await service.AddTagAsync(id, new AddPhysicalInventoryTagRequestModel
            {
                LocationId = s.ChildId, PartId = s.OtherPartId, Quantity = 5m, Notes = "Found on the shelf",
            }, 2);
            var review = await service.SubmitForReviewAsync(id);
            review.Status.Should().Be(PhysicalInventoryStatus.Review);
            review.Tags.Should().OnlyContain(t => t.FrozenQuantity != null);

            var variance = await service.GetVarianceAsync(id);
            variance.ShortageValue.Should().Be(-100m);
            variance.OverageValue.Should().Be(10m);
            variance.NetVarianceValue.Should().Be(-90m);
        }

        await using (var db = fixture.CreateContext())
        {
            var posted = await Service(db).PostAsync(id, 1);
            posted.Status.Should().Be(PhysicalInventoryStatus.Posted);
            posted.TotalVarianceValue.Should().Be(-90m);
        }

        await using (var db = fixture.CreateContext())
        {
            (await db.BinContents.SingleAsync(c => c.Id == s.ChildBinId)).Quantity.Should().Be(90m);
            (await db.BinContents.SingleAsync(c => c.EntityId == s.OtherPartId && c.RemovedAt == null)).Quantity.Should().Be(5m);
            (await db.BinMovements.CountAsync(m => m.PhysicalInventoryId == id && m.Reason == BinMovementReason.PhysicalInventory))
                .Should().Be(2);

            var store = await db.InventoryValuations.SingleAsync(v => v.PartId == s.PartId);
            store.OnHandQuantity.Should().Be(140m);
            store.TotalValue.Should().Be(1400m);

            var tags = await db.PhysicalInventoryTags.Where(t => t.PhysicalInventoryId == id).OrderBy(t => t.TagNumber).ToListAsync();
            var shortage = await InventoryLedgerTestBook.LinesAsync(db, $"Inventory:{PhysicalInventoryService.TagSourceType}:{tags[0].Id}:ADJUST");
            shortage.Single(l => l.GlAccountId == InventoryLedgerTestBook.WritedownId).Debit.Should().Be(100m);
            shortage.Single(l => l.GlAccountId == InventoryLedgerTestBook.RawId).Credit.Should().Be(100m);
            var overage = await InventoryLedgerTestBook.LinesAsync(db, $"Inventory:{PhysicalInventoryService.TagSourceType}:{tags[1].Id}:ADJUST");
            overage.Single(l => l.GlAccountId == InventoryLedgerTestBook.RawId).Debit.Should().Be(10m);
        }

        await using (var db = fixture.CreateContext())
        {
            await MoveOutAsync(db, s.ChildBinId, 1m);
        }
    }

    [Fact]
    public async Task A_count_outside_the_tolerance_must_be_recounted_before_review()
    {
        var s = await SeedAsync();
        await using var db = fixture.CreateContext();
        var id = await CreateAndFreezeAsync(db, s.ParentId, tolerance: 5m);
        var service = Service(db);
        var tag = (await service.GetAsync(id)).Tags.Single();

        (await service.EnterCountAsync(id, tag.Id, new EnterPhysicalCountRequestModel { Quantity = 80m }, 2))
            .Status.Should().Be(PhysicalInventoryTagStatus.RecountRequired);
        var early = () => service.SubmitForReviewAsync(id);
        await early.Should().ThrowAsync<InvalidOperationException>().WithMessage("*1 awaiting a recount*");

        var recounted = await service.EnterCountAsync(id, tag.Id, new EnterPhysicalCountRequestModel { Quantity = 95m }, 3);
        recounted.Status.Should().Be(PhysicalInventoryTagStatus.Recounted);
        recounted.FinalQuantity.Should().Be(95m);
        (await service.SubmitForReviewAsync(id)).Status.Should().Be(PhysicalInventoryStatus.Review);
    }

    [Fact]
    public async Task Two_inventories_cannot_freeze_overlapping_scopes()
    {
        var s = await SeedAsync();
        await using var db = fixture.CreateContext();
        await CreateAndFreezeAsync(db, s.ParentId);
        var service = Service(db);
        var second = await service.CreateAsync(new CreatePhysicalInventoryRequestModel { ScopeLocationId = s.ChildId, CountDate = new DateOnly(2026, 6, 30) }, 1);

        var freeze = () => service.FreezeAsync(second.Id, 1);

        await freeze.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already has stock in this scope frozen*");
    }
}
