using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Inventory;
using Forge.Api.Features.Quality;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

using static Forge.Tests.Helpers.InventoryLedgerTestBook;

namespace Forge.Tests.Handlers.Inventory;

public class InterPlantTransferStockTests
{
    private const int UserId = 7;

    private sealed record Seeded(AppDbContext Db, InterPlantTransfer Transfer, int PartId, int SourceBinId, int DestinationBinId);

    private static async Task<Seeded> SeedAsync(
        decimal onHand = 10000m, decimal transferQty = 500m, decimal reserved = 0m,
        BinContentStatus status = BinContentStatus.Stored, string? lot = null, bool withLedger = false)
    {
        var db = TestDbContextFactory.Create();
        if (withLedger)
            await InventoryLedgerTestBook.SeedAsync(db);
        var part = new Part { PartNumber = "INS-M4", Description = "M4 insert", InventoryClass = InventoryClass.Raw, ManualCostOverride = 0.2m };
        var source = new StorageLocation { Name = "A3", LocationType = LocationType.Bin, IsActive = true };
        var destination = new StorageLocation { Name = "AX-2", LocationType = LocationType.Bin, IsActive = true };
        db.Parts.Add(part);
        db.StorageLocations.AddRange(source, destination);
        await db.SaveChangesAsync();
        db.BinContents.Add(new BinContent
        {
            LocationId = source.Id, EntityType = "part", EntityId = part.Id, Quantity = onHand,
            ReservedQuantity = reserved, Status = status, LotNumber = lot, PlacedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        var transfer = new InterPlantTransfer
        {
            TransferNumber = "IPT-20261017-00001", FromPlantId = 1, ToPlantId = 2,
            Lines = [new InterPlantTransferLine { PartId = part.Id, Quantity = transferQty, FromLocationId = source.Id, ToLocationId = destination.Id }],
        };
        db.InterPlantTransfers.Add(transfer);
        await db.SaveChangesAsync();
        return new Seeded(db, transfer, part.Id, source.Id, destination.Id);
    }

    private static Task ShipAsync(AppDbContext db, int transferId)
        => new ShipInterPlantTransferHandler(db, new SystemClock(), UserAccessor(UserId))
            .Handle(new ShipInterPlantTransferCommand(transferId, new ShipTransferRequestModel("1Z999")), CancellationToken.None);

    private static Task ReceiveAsync(AppDbContext db, int transferId, List<ReceiveTransferLineRequestModel> lines, bool withPosting = false)
        => new ReceiveInterPlantTransferHandler(db, new SystemClock(), UserAccessor(UserId), withPosting ? AdjustmentPosting(db) : null)
            .Handle(new ReceiveInterPlantTransferCommand(transferId, lines), CancellationToken.None);

    private static Task<decimal> OnHandAsync(AppDbContext db, int locationId)
        => db.BinContents.Where(b => b.LocationId == locationId && b.RemovedAt == null).SumAsync(b => b.Quantity);

    [Fact]
    public async Task Ship_DrawsTheSourceBin_AndRecordsTheShipper()
    {
        var s = await SeedAsync();
        using var _ = s.Db;

        await ShipAsync(s.Db, s.Transfer.Id);

        (await OnHandAsync(s.Db, s.SourceBinId)).Should().Be(9500m);
        (await OnHandAsync(s.Db, s.DestinationBinId)).Should().Be(0m);
        var movement = await s.Db.BinMovements.SingleAsync();
        movement.Reason.Should().Be(BinMovementReason.Transfer);
        movement.Quantity.Should().Be(500m);
        movement.FromLocationId.Should().Be(s.SourceBinId);
        movement.ToLocationId.Should().BeNull();
        s.Transfer.Status.Should().Be(InterPlantTransferStatus.Shipped);
        s.Transfer.ShippedById.Should().Be(UserId);
    }

    [Fact]
    public async Task ShipThenReceive_MovesTheStock_AndKeepsTotalOnHand()
    {
        var s = await SeedAsync();
        using var _ = s.Db;

        await ShipAsync(s.Db, s.Transfer.Id);
        await ReceiveAsync(s.Db, s.Transfer.Id, [new ReceiveTransferLineRequestModel(s.PartId, 500m)]);

        (await OnHandAsync(s.Db, s.SourceBinId)).Should().Be(9500m);
        (await OnHandAsync(s.Db, s.DestinationBinId)).Should().Be(500m);
        s.Db.BinMovements.Should().Contain(m => m.ToLocationId == s.DestinationBinId && m.Quantity == 500m);
        s.Transfer.Status.Should().Be(InterPlantTransferStatus.Received);
        s.Transfer.ReceivedById.Should().Be(UserId);
        s.Transfer.Lines.Single().ReceivedQuantity.Should().Be(500m);
    }

    [Fact]
    public async Task Receive_WithoutLines_ReceivesTheShippedQuantity()
    {
        var s = await SeedAsync();
        using var _ = s.Db;

        await ShipAsync(s.Db, s.Transfer.Id);
        await ReceiveAsync(s.Db, s.Transfer.Id, []);

        (await OnHandAsync(s.Db, s.DestinationBinId)).Should().Be(500m);
    }

    [Fact]
    public async Task Receive_KeepsTheLotThatShipped()
    {
        var s = await SeedAsync(lot: "H-55821");
        using var _ = s.Db;

        await ShipAsync(s.Db, s.Transfer.Id);
        await ReceiveAsync(s.Db, s.Transfer.Id, []);

        var placed = await s.Db.BinContents.SingleAsync(b => b.LocationId == s.DestinationBinId);
        placed.LotNumber.Should().Be("H-55821");
    }

    [Fact]
    public async Task Receive_Short_RecordsTheShortage_AndPostsItUnderFullGl()
    {
        var s = await SeedAsync(withLedger: true);
        using var _ = s.Db;

        await ShipAsync(s.Db, s.Transfer.Id);
        await ReceiveAsync(s.Db, s.Transfer.Id, [new ReceiveTransferLineRequestModel(s.PartId, 480m)], withPosting: true);

        (await OnHandAsync(s.Db, s.DestinationBinId)).Should().Be(480m);
        s.Db.BinMovements.Should().ContainSingle(m => m.Reason == BinMovementReason.Adjustment && m.Quantity == -20m);
        var line = s.Transfer.Lines.Single();
        var journal = await LinesAsync(s.Db, $"Inventory:InterPlantTransferLine:{line.Id}:ADJUST");
        journal.Single(l => l.GlAccountId == WritedownId).Debit.Should().Be(4m);
        journal.Single(l => l.GlAccountId == RawId).Credit.Should().Be(4m);
    }

    [Fact]
    public async Task Receive_MoreThanShipped_Throws()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        await ShipAsync(s.Db, s.Transfer.Id);

        var act = () => ReceiveAsync(s.Db, s.Transfer.Id, [new ReceiveTransferLineRequestModel(s.PartId, 501m)]);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Receive between 0 and 500 of INS-M4.");
        s.Transfer.Status.Should().Be(InterPlantTransferStatus.Shipped);
    }

    [Fact]
    public async Task Ship_MoreThanFree_Throws_AndLeavesTheBin()
    {
        var s = await SeedAsync(onHand: 600m, reserved: 200m);
        using var _ = s.Db;

        var act = () => ShipAsync(s.Db, s.Transfer.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Cannot ship 500 of INS-M4: only 400 is free to move in the source bin.");
        s.Transfer.Status.Should().Be(InterPlantTransferStatus.Draft);
    }

    [Fact]
    public async Task Ship_FromQualityHold_Throws()
    {
        var s = await SeedAsync(status: BinContentStatus.QcHold, lot: "R-26-117");
        using var _ = s.Db;

        var act = () => ShipAsync(s.Db, s.Transfer.Id);

        await act.Should().ThrowAsync<QualityHoldException>();
    }

    [Fact]
    public async Task Ship_WithoutSourceBin_Throws()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        s.Transfer.Lines.Single().FromLocationId = null;
        await s.Db.SaveChangesAsync();

        var act = () => ShipAsync(s.Db, s.Transfer.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Choose the bin INS-M4 ships from before shipping this transfer.");
    }

    [Fact]
    public async Task Create_TwoInTheSameSecond_GetDistinctNumbers()
    {
        using var db = TestDbContextFactory.Create();
        db.Plants.AddRange(new Plant { Id = 1, Code = "A", Name = "Plant A" }, new Plant { Id = 2, Code = "B", Name = "Plant B" });
        var part = new Part { PartNumber = "INS-M4", Description = "M4 insert" };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        var clock = new Moq.Mock<Forge.Core.Interfaces.IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 17, 14, 16, 52, TimeSpan.Zero));
        var handler = new CreateInterPlantTransferHandler(db, clock.Object);
        var request = new CreateInterPlantTransferRequestModel(1, 2, null,
            [new CreateInterPlantTransferLineRequestModel(part.Id, 5m, null, null, null)]);

        var first = await handler.Handle(new CreateInterPlantTransferCommand(request), CancellationToken.None);
        var second = await handler.Handle(new CreateInterPlantTransferCommand(request), CancellationToken.None);

        first.TransferNumber.Should().Be("IPT-20261017-00001");
        second.TransferNumber.Should().Be("IPT-20261017-00002");
    }
}
