using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs.ProductionRuns;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

/// <summary>
/// job-complete→FG: receiving a completed production run stocks the good output into an FG bin and stamps the
/// run received (idempotent). GL posting is null here (operational behavior only).
/// </summary>
public class ReceiveProductionRunToStockHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ReceiveProductionRunToStockHandler _handler;

    public ReceiveProductionRunToStockHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new ReceiveProductionRunToStockHandler(_db, new SystemClock(), new InventoryRepository(_db), posting: null);
    }

    private async Task<ProductionRun> SeedRunAsync(
        int completedQty, ProductionRunStatus status = ProductionRunStatus.Completed, int? defaultBinId = null)
    {
        var part = new Part
        {
            PartNumber = "P-FG-001", Name = "FG Part", InventoryClass = InventoryClass.FinishedGood,
            DefaultBinId = defaultBinId,
        };
        _db.Parts.Add(part);
        var job = new Job { JobNumber = "JOB-FG-001", Description = "Test Job" };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        var run = new ProductionRun
        {
            JobId = job.Id,
            PartId = part.Id,
            RunNumber = $"RUN-{Guid.NewGuid():N}",
            TargetQuantity = 10,
            CompletedQuantity = completedQty,
            Status = status,
            CompletedAt = status == ProductionRunStatus.Completed ? DateTimeOffset.UtcNow : null,
        };
        _db.ProductionRuns.Add(run);
        await _db.SaveChangesAsync();
        return run;
    }

    [Fact]
    public async Task Handle_CompletedRun_StocksFgBin_AndStampsReceived()
    {
        var run = await SeedRunAsync(completedQty: 8);

        await _handler.Handle(new ReceiveProductionRunToStockCommand(run.JobId, run.Id, ReceivedByUserId: 1), default);

        _db.ChangeTracker.Clear();
        var stocked = await _db.BinContents
            .FirstOrDefaultAsync(b => b.EntityType == "part" && b.EntityId == run.PartId);
        stocked.Should().NotBeNull("the good output must be stocked into an FG bin");
        stocked!.Quantity.Should().Be(8m);

        var after = await _db.ProductionRuns.FindAsync(run.Id);
        after!.ReceivedToStockAt.Should().NotBeNull();
        after.ReceivedQuantity.Should().Be(8);

        (await _db.BinMovements.CountAsync(m => m.Reason == BinMovementReason.Receive && m.EntityId == run.PartId))
            .Should().Be(1);
    }

    [Fact]
    public async Task Handle_RunNotCompleted_Throws()
    {
        var run = await SeedRunAsync(completedQty: 8, status: ProductionRunStatus.InProgress);

        var act = () => _handler.Handle(
            new ReceiveProductionRunToStockCommand(run.JobId, run.Id, ReceivedByUserId: 1), default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Only completed*");
    }

    [Fact]
    public async Task Handle_ZeroGoodQuantity_Throws()
    {
        var run = await SeedRunAsync(completedQty: 0);

        var act = () => _handler.Handle(
            new ReceiveProductionRunToStockCommand(run.JobId, run.Id, ReceivedByUserId: 1), default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no good completed quantity*");
    }

    [Fact]
    public async Task Handle_AlreadyReceived_IsIdempotentNoOp()
    {
        var run = await SeedRunAsync(completedQty: 8);

        await _handler.Handle(new ReceiveProductionRunToStockCommand(run.JobId, run.Id, ReceivedByUserId: 1), default);
        await _handler.Handle(new ReceiveProductionRunToStockCommand(run.JobId, run.Id, ReceivedByUserId: 1), default);

        _db.ChangeTracker.Clear();
        // Stock not doubled; exactly one Receive movement.
        (await _db.BinContents.SingleAsync(b => b.EntityType == "part" && b.EntityId == run.PartId))
            .Quantity.Should().Be(8m);
        (await _db.BinMovements.CountAsync(m => m.Reason == BinMovementReason.Receive && m.EntityId == run.PartId))
            .Should().Be(1);
    }

    private async Task<StorageLocation> SeedBinAsync(string name, bool isActive = true)
    {
        var bin = new StorageLocation { Name = name, LocationType = LocationType.Bin, IsActive = isActive };
        _db.StorageLocations.Add(bin);
        await _db.SaveChangesAsync();
        return bin;
    }

    private async Task<int> StockedLocationAsync(int partId)
    {
        _db.ChangeTracker.Clear();
        var content = await _db.BinContents.SingleAsync(b => b.EntityType == "part" && b.EntityId == partId);
        var movement = await _db.BinMovements.SingleAsync(m => m.EntityType == "part" && m.EntityId == partId);
        movement.ToLocationId.Should().Be(content.LocationId);
        return content.LocationId;
    }

    [Fact]
    public async Task Handle_RequestedActiveBin_IsUsed()
    {
        var defaultBin = await SeedBinAsync("B-01");
        var requested = await SeedBinAsync("A-03");
        var run = await SeedRunAsync(completedQty: 8, defaultBinId: defaultBin.Id);

        await _handler.Handle(
            new ReceiveProductionRunToStockCommand(run.JobId, run.Id, ReceivedByUserId: 1, LocationId: requested.Id),
            default);

        (await StockedLocationAsync(run.PartId)).Should().Be(requested.Id);
    }

    [Fact]
    public async Task Handle_InactiveRequestedBin_FallsBackToThePartDefaultBin()
    {
        var defaultBin = await SeedBinAsync("B-01");
        var inactive = await SeedBinAsync("A-03", isActive: false);
        var run = await SeedRunAsync(completedQty: 8, defaultBinId: defaultBin.Id);

        await _handler.Handle(
            new ReceiveProductionRunToStockCommand(run.JobId, run.Id, ReceivedByUserId: 1, LocationId: inactive.Id),
            default);

        (await StockedLocationAsync(run.PartId)).Should().Be(defaultBin.Id);
    }

    [Fact]
    public async Task Handle_NoRequestedOrDefaultBin_UsesFinishedGoodsNotTheFirstBin()
    {
        var unrelated = await SeedBinAsync("A-01");
        var run = await SeedRunAsync(completedQty: 8);

        await _handler.Handle(new ReceiveProductionRunToStockCommand(run.JobId, run.Id, ReceivedByUserId: 1), default);

        var locationId = await StockedLocationAsync(run.PartId);
        locationId.Should().NotBe(unrelated.Id);
        (await _db.StorageLocations.SingleAsync(l => l.Id == locationId)).Name.Should().Be("Finished Goods");
    }

    [Fact]
    public async Task Handle_ExistingFinishedGoodsBin_IsReused()
    {
        await SeedBinAsync("A-01");
        var fg = await SeedBinAsync("Finished Goods");
        var run = await SeedRunAsync(completedQty: 8);

        await _handler.Handle(new ReceiveProductionRunToStockCommand(run.JobId, run.Id, ReceivedByUserId: 1), default);

        (await StockedLocationAsync(run.PartId)).Should().Be(fg.Id);
        (await _db.StorageLocations.CountAsync(l => l.Name == "Finished Goods")).Should().Be(1);
    }

    public void Dispose() => _db.Dispose();
}
