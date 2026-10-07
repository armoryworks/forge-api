using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Scanner;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Scanner;

public class ScannerLotStockTests : IDisposable
{
    private const int UserId = 7;
    private const string Pin = "2468";

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly IClock _clock;
    private readonly IHttpContextAccessor _accessor;

    public ScannerLotStockTests()
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
        _clock = clock.Object;
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, UserId.ToString()), new Claim(ClaimTypes.Name, "Scanner")], "Test")),
        });
        _accessor = accessor.Object;
    }

    private async Task<(int PartId, int BinId, int OtherBinId)> SeedAsync(decimal unlotted, decimal lotted, decimal lottedReserved = 0m)
    {
        var part = new Part { PartNumber = "BAR-12", Description = "12 ft bar" };
        _db.Parts.Add(part);
        var bin = new StorageLocation { Name = "A1", LocationType = LocationType.Bin, IsActive = true };
        var other = new StorageLocation { Name = "B1", LocationType = LocationType.Bin, IsActive = true };
        _db.StorageLocations.AddRange(bin, other);
        await _db.SaveChangesAsync();

        _db.BinContents.Add(new BinContent
        {
            LocationId = bin.Id, EntityType = "part", EntityId = part.Id, Quantity = lotted, LotNumber = "HEAT-A",
            ReservedQuantity = lottedReserved, PlacedAt = _clock.UtcNow.AddDays(-2),
        });
        _db.BinContents.Add(new BinContent
        {
            LocationId = bin.Id, EntityType = "part", EntityId = part.Id, Quantity = unlotted,
            PlacedAt = _clock.UtcNow.AddDays(-1),
        });
        _db.Users.Add(new ApplicationUser
        {
            Id = UserId, UserName = "scanner", Email = "scanner@forge.local", FirstName = "Scan", LastName = "Ner",
            PinHash = new PasswordHasher<object>().HashPassword(null!, Pin),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (part.Id, bin.Id, other.Id);
    }

    private async Task<int> SeedJobAsync()
    {
        var job = new Job { JobNumber = "J-1", Title = "Frame weldment", TrackTypeId = 1, CurrentStageId = 1 };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return job.Id;
    }

    private Task<List<BinContent>> ActiveAsync(int partId, int binId)
        => _db.BinContents.AsNoTracking()
            .Where(b => b.EntityId == partId && b.LocationId == binId && b.RemovedAt == null)
            .ToListAsync();

    private Task ReverseAsync(int scanLogId)
        => new ReverseScanActionHandler(_db, _clock, _accessor).Handle(
            new ReverseScanActionCommand(new ScanReversalRequestModel(scanLogId, Pin)), CancellationToken.None);

    [Fact]
    public async Task Count_BelowTheLocationTotal_DrawsUnlottedThenLotAndMatchesTheCount()
    {
        var (partId, binId, _) = await SeedAsync(unlotted: 5m, lotted: 10m);

        await new ExecuteScanCountHandler(_db, _clock, _accessor).Handle(
            new ExecuteScanCountCommand(new ScanCountRequestModel(partId, binId, 3m)), CancellationToken.None);

        _db.ChangeTracker.Clear();
        var rows = await ActiveAsync(partId, binId);
        rows.Should().ContainSingle().Which.LotNumber.Should().Be("HEAT-A");
        rows.Sum(r => r.Quantity).Should().Be(3m);
        var movements = await _db.BinMovements.Where(m => m.EntityId == partId).ToListAsync();
        movements.Should().HaveCount(2);
        movements.Single(m => m.LotNumber == null).Quantity.Should().Be(5m);
        movements.Single(m => m.LotNumber == "HEAT-A").Quantity.Should().Be(7m);
    }

    [Fact]
    public async Task Count_AboveTheLocationTotal_AddsTheGainToTheUnlottedRow()
    {
        var (partId, binId, _) = await SeedAsync(unlotted: 5m, lotted: 10m);

        await new ExecuteScanCountHandler(_db, _clock, _accessor).Handle(
            new ExecuteScanCountCommand(new ScanCountRequestModel(partId, binId, 18m)), CancellationToken.None);

        _db.ChangeTracker.Clear();
        var rows = await ActiveAsync(partId, binId);
        rows.Single(r => r.LotNumber == null).Quantity.Should().Be(8m);
        rows.Single(r => r.LotNumber == "HEAT-A").Quantity.Should().Be(10m);
    }

    [Fact]
    public async Task Count_BelowReserved_IsRejected()
    {
        var (partId, binId, _) = await SeedAsync(unlotted: 5m, lotted: 10m, lottedReserved: 8m);

        var act = () => new ExecuteScanCountHandler(_db, _clock, _accessor).Handle(
            new ExecuteScanCountCommand(new ScanCountRequestModel(partId, binId, 6m)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Issue_SpreadOverLots_DrawsAcrossRowsWithOneMovementEach()
    {
        var (partId, binId, _) = await SeedAsync(unlotted: 5m, lotted: 10m);
        var jobId = await SeedJobAsync();

        await new ExecuteScanIssueHandler(_db, _clock, _accessor).Handle(
            new ExecuteScanIssueCommand(new ScanIssueRequestModel(partId, jobId, 12m, binId)), CancellationToken.None);

        _db.ChangeTracker.Clear();
        var rows = await ActiveAsync(partId, binId);
        rows.Should().ContainSingle().Which.Quantity.Should().Be(3m);
        var movements = await _db.BinMovements.Where(m => m.EntityId == partId).ToListAsync();
        movements.Select(m => (m.LotNumber, m.Quantity)).Should().BeEquivalentTo(
            new (string?, decimal)[] { (null, 5m), ("HEAT-A", 7m) });
    }

    [Fact]
    public async Task Issue_MoreThanTheFreeTotal_IsRejected()
    {
        var (partId, binId, _) = await SeedAsync(unlotted: 5m, lotted: 10m, lottedReserved: 4m);
        var jobId = await SeedJobAsync();

        var act = () => new ExecuteScanIssueHandler(_db, _clock, _accessor).Handle(
            new ExecuteScanIssueCommand(new ScanIssueRequestModel(partId, jobId, 12m, binId)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Move_SpreadOverLots_KeepsEachLotSeparateAtTheDestination()
    {
        var (partId, binId, otherId) = await SeedAsync(unlotted: 5m, lotted: 10m);

        await new ExecuteScanMoveHandler(_db, _clock, _accessor).Handle(
            new ExecuteScanMoveCommand(new ScanMoveRequestModel(partId, binId, otherId, 12m)), CancellationToken.None);

        _db.ChangeTracker.Clear();
        (await ActiveAsync(partId, binId)).Should().ContainSingle().Which.Quantity.Should().Be(3m);
        var destination = await ActiveAsync(partId, otherId);
        destination.Single(r => r.LotNumber == null).Quantity.Should().Be(5m);
        destination.Single(r => r.LotNumber == "HEAT-A").Quantity.Should().Be(7m);
    }

    [Fact]
    public async Task ReverseMove_PutsEachLotBack()
    {
        var (partId, binId, otherId) = await SeedAsync(unlotted: 5m, lotted: 10m);
        var logId = await new ExecuteScanMoveHandler(_db, _clock, _accessor).Handle(
            new ExecuteScanMoveCommand(new ScanMoveRequestModel(partId, binId, otherId, 12m)), CancellationToken.None);
        _db.ChangeTracker.Clear();

        await ReverseAsync(logId);

        _db.ChangeTracker.Clear();
        var rows = await ActiveAsync(partId, binId);
        rows.Single(r => r.LotNumber == null).Quantity.Should().Be(5m);
        rows.Single(r => r.LotNumber == "HEAT-A").Quantity.Should().Be(10m);
        (await ActiveAsync(partId, otherId)).Should().BeEmpty();
    }

    [Fact]
    public async Task ReverseCount_RestoresTheLotsTheCountDrew()
    {
        var (partId, binId, _) = await SeedAsync(unlotted: 5m, lotted: 10m);
        var logId = await new ExecuteScanCountHandler(_db, _clock, _accessor).Handle(
            new ExecuteScanCountCommand(new ScanCountRequestModel(partId, binId, 3m)), CancellationToken.None);
        _db.ChangeTracker.Clear();

        await ReverseAsync(logId);

        _db.ChangeTracker.Clear();
        var rows = await ActiveAsync(partId, binId);
        rows.Single(r => r.LotNumber == null).Quantity.Should().Be(5m);
        rows.Single(r => r.LotNumber == "HEAT-A").Quantity.Should().Be(10m);
    }

    [Fact]
    public async Task ReverseIssue_ReturnsStockToTheLotsItCameFrom()
    {
        var (partId, binId, _) = await SeedAsync(unlotted: 5m, lotted: 10m);
        var jobId = await SeedJobAsync();
        var logId = await new ExecuteScanIssueHandler(_db, _clock, _accessor).Handle(
            new ExecuteScanIssueCommand(new ScanIssueRequestModel(partId, jobId, 12m, binId)), CancellationToken.None);
        _db.ChangeTracker.Clear();

        await ReverseAsync(logId);

        _db.ChangeTracker.Clear();
        var rows = await ActiveAsync(partId, binId);
        rows.Single(r => r.LotNumber == null).Quantity.Should().Be(5m);
        rows.Single(r => r.LotNumber == "HEAT-A").Quantity.Should().Be(10m);
    }

    [Fact]
    public async Task ReverseReceive_TakesStockFromTheUnlottedRowItAddedTo()
    {
        var (partId, binId, _) = await SeedAsync(unlotted: 0m, lotted: 10m);
        var po = new PurchaseOrder { PONumber = "PO-SCAN-2", VendorId = 1, Status = PurchaseOrderStatus.Submitted };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();
        var line = new PurchaseOrderLine { PurchaseOrderId = po.Id, PartId = partId, OrderedQuantity = 5m, UnitPrice = 30m };
        _db.PurchaseOrderLines.Add(line);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var logId = await new ExecuteScanReceiveHandler(_db, _clock, _accessor).Handle(
            new ExecuteScanReceiveCommand(new ScanReceiveRequestModel(partId, line.Id, 4m, binId)), CancellationToken.None);
        _db.ChangeTracker.Clear();

        await ReverseAsync(logId);

        _db.ChangeTracker.Clear();
        var rows = await ActiveAsync(partId, binId);
        rows.Single(r => r.LotNumber == "HEAT-A").Quantity.Should().Be(10m);
        rows.Where(r => r.LotNumber == null).Sum(r => r.Quantity).Should().Be(0m);
    }

    public void Dispose() => _db.Dispose();
}
