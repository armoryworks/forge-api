using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.Inventory;
using Forge.Api.Features.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

/// <summary>
/// INV-SF2 / F-033 acceptance tests for CreateMaterialIssue source-state guard.
/// Proves that archived jobs are hard-blocked from receiving material transactions.
/// </summary>
public class CreateMaterialIssueHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CreateMaterialIssueHandler _handler;

    public CreateMaterialIssueHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new CreateMaterialIssueHandler(_db);
    }

    private async Task<(Job job, Part part)> SeedAsync(bool archived = false)
    {
        var part = new Part { PartNumber = "P-MI-001", Name = "Test Part" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        var job = new Job
        {
            JobNumber = "JOB-MI-001",
            Description = "Test Job",
            IsArchived = archived,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        return (job, part);
    }

    // ── INV-SF2: archived-job source-state guard ──────────────────────────────

    [Fact]
    public async Task Handle_ArchivedJob_ThrowsInvalidOperation_INV_SF2()
    {
        var (job, part) = await SeedAsync(archived: true);

        var act = () => _handler.Handle(new CreateMaterialIssueCommand(
            JobId: job.Id,
            PartId: part.Id,
            OperationId: null,
            Quantity: 5m,
            BinContentId: null,
            StorageLocationId: null,
            LotNumber: null,
            IssueType: MaterialIssueType.Issue,
            Notes: null,
            IssuedById: 1), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*archived job*");

        // No issue record must be written
        var issueCount = await _db.MaterialIssues.CountAsync();
        issueCount.Should().Be(0, "archived-job guard must abort before writing");
    }

    [Fact]
    public async Task Handle_NoBinAndNoLocation_IsRefusedAndWritesNothing()
    {
        var (job, part) = await SeedAsync(archived: false);

        var act = () => _handler.Handle(Command(job, part, 3m), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Choose the bin or location*");
        (await _db.MaterialIssues.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ActiveJob_StockDrawnByCaller_CreatesIssueRecord()
    {
        var (job, part) = await SeedAsync(archived: false);

        var result = await _handler.Handle(
            Command(job, part, 3m) with { StockDrawnByCaller = true }, CancellationToken.None);

        result.Quantity.Should().Be(3m);
        result.IssueType.Should().Be(MaterialIssueType.Issue);
        var issue = await _db.MaterialIssues.SingleAsync();
        issue.JobId.Should().Be(job.Id);
        issue.PartId.Should().Be(part.Id);
    }

    [Fact]
    public async Task Handle_ByLocation_DrawsTheStockOneIssuePerLot()
    {
        var (job, part) = await SeedAsync();
        var bin = await BinAsync();
        await StockAsync(part, bin, 4m, "L-1", daysAgo: 3);
        await StockAsync(part, bin, 10m, "L-2", daysAgo: 1);

        var result = await _handler.Handle(
            Command(job, part, 6m) with { StorageLocationId = bin.Id }, CancellationToken.None);

        result.Quantity.Should().Be(6m);
        var issues = await _db.MaterialIssues.AsNoTracking().OrderBy(m => m.Id).ToListAsync();
        issues.Select(m => (m.LotNumber, m.Quantity, m.StorageLocationId)).Should().Equal(
            ("L-1", 4m, (int?)bin.Id), ("L-2", 2m, (int?)bin.Id));
        issues.Should().OnlyContain(m => m.BinContentId != null);
        (await OnHandAsync(part, bin)).Should().Be(8m);
    }

    [Fact]
    public async Task Handle_ByLocationAndLot_DrawsOnlyThatLot_AndRefusesMoreThanItHolds()
    {
        var (job, part) = await SeedAsync();
        var bin = await BinAsync();
        await StockAsync(part, bin, 4m, "L-1", daysAgo: 3);
        await StockAsync(part, bin, 10m, "L-2", daysAgo: 1);

        var tooMuch = () => _handler.Handle(
            Command(job, part, 5m) with { StorageLocationId = bin.Id, LotNumber = "L-1" }, CancellationToken.None);
        await tooMuch.Should().ThrowAsync<InvalidOperationException>().WithMessage("Insufficient quantity*");

        await _handler.Handle(
            Command(job, part, 3m) with { StorageLocationId = bin.Id, LotNumber = "L-2" }, CancellationToken.None);
        (await _db.MaterialIssues.AsNoTracking().SingleAsync()).LotNumber.Should().Be("L-2");
        (await OnHandAsync(part, bin)).Should().Be(11m);
    }

    [Fact]
    public async Task Handle_ScrapAndReturnByLocation_MoveTheStock()
    {
        var (job, part) = await SeedAsync();
        var bin = await BinAsync();
        await StockAsync(part, bin, 10m, "L-1");

        await _handler.Handle(
            Command(job, part, 2m) with { StorageLocationId = bin.Id, LotNumber = "L-1", IssueType = MaterialIssueType.Scrap },
            CancellationToken.None);
        (await OnHandAsync(part, bin)).Should().Be(8m);

        await _handler.Handle(
            Command(job, part, 5m) with { StorageLocationId = bin.Id, LotNumber = "L-1", IssueType = MaterialIssueType.Return },
            CancellationToken.None);
        (await OnHandAsync(part, bin)).Should().Be(13m);
    }

    [Fact]
    public async Task Handle_ReturnToABinRowDrawnEmpty_PutsTheStockBackOnHand()
    {
        var (job, part) = await SeedAsync();
        var bin = await BinAsync();
        var row = await StockAsync(part, bin, 0m, "L-1");
        row.RemovedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        await _handler.Handle(
            Command(job, part, 4m) with { BinContentId = row.Id, IssueType = MaterialIssueType.Return },
            CancellationToken.None);

        (await OnHandAsync(part, bin)).Should().Be(4m);
        var issue = await _db.MaterialIssues.AsNoTracking().SingleAsync();
        issue.LotNumber.Should().Be("L-1");
        issue.BinContentId.Should().NotBe(row.Id);
    }

    [Fact]
    public async Task Handle_BinOfAnotherPart_IsRefused()
    {
        var (job, part) = await SeedAsync();
        var other = new Part { PartNumber = "P-OTHER", Name = "Other" };
        _db.Parts.Add(other);
        await _db.SaveChangesAsync();
        var bin = await BinAsync();
        var row = await StockAsync(other, bin, 10m, null);

        var act = () => _handler.Handle(Command(job, part, 1m) with { BinContentId = row.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not hold part*");
    }

    [Fact]
    public async Task Handle_ExpiredLot_IsRefusedByBinAndByLocation_UnlessOverridden()
    {
        var (job, part) = await SeedAsync();
        var bin = await BinAsync();
        var row = await StockAsync(part, bin, 10m, "R-26-119");
        _db.LotRecords.Add(new LotRecord
        {
            LotNumber = "R-26-119", PartId = part.Id, Quantity = 10,
            ExpirationDate = DateTimeOffset.UtcNow.AddDays(-7),
        });
        await _db.SaveChangesAsync();

        var byBin = () => _handler.Handle(Command(job, part, 1m) with { BinContentId = row.Id }, CancellationToken.None);
        await byBin.Should().ThrowAsync<LotExpiredException>().WithMessage("Lot R-26-119 expired on *");
        var byLocation = () => _handler.Handle(
            Command(job, part, 1m) with { StorageLocationId = bin.Id }, CancellationToken.None);
        await byLocation.Should().ThrowAsync<LotExpiredException>();
        (await _db.MaterialIssues.AnyAsync()).Should().BeFalse();

        await _handler.Handle(
            Command(job, part, 1m) with { BinContentId = row.Id, AllowExpiredLot = true }, CancellationToken.None);

        (await _db.MaterialIssues.CountAsync()).Should().Be(1);
        (await _db.JobActivityLogs.AsNoTracking().SingleAsync()).Description.Should().Contain("R-26-119").And.Contain("override");
    }

    [Fact]
    public async Task Handle_LotWithAFutureExpiry_Issues()
    {
        var (job, part) = await SeedAsync();
        var bin = await BinAsync();
        var row = await StockAsync(part, bin, 10m, "FRESH");
        _db.LotRecords.Add(new LotRecord
        {
            LotNumber = "FRESH", PartId = part.Id, Quantity = 10, ExpirationDate = DateTimeOffset.UtcNow.AddDays(30),
        });
        await _db.SaveChangesAsync();

        await _handler.Handle(Command(job, part, 1m) with { BinContentId = row.Id }, CancellationToken.None);

        (await _db.MaterialIssues.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task IssuableMaterials_ListTheBomAndApprovedAlternatesWithRequiredAndIssued()
    {
        var (job, part) = await SeedAsync();
        var parent = new Part { PartNumber = "FG-1", Name = "Assembly" };
        var alternate = new Part { PartNumber = "P-MI-ALT", Name = "Alternate" };
        _db.Parts.AddRange(parent, alternate);
        await _db.SaveChangesAsync();
        job.PartId = parent.Id;
        _db.JobParts.Add(new JobPart { JobId = job.Id, PartId = parent.Id, Quantity = 10 });
        _db.BOMLines.Add(new BOMLine { ParentPartId = parent.Id, ChildPartId = part.Id, Quantity = 2m });
        _db.PartAlternates.Add(new PartAlternate { PartId = part.Id, AlternatePartId = alternate.Id, IsApproved = true });
        await _db.SaveChangesAsync();
        await _handler.Handle(Command(job, part, 3m) with { StockDrawnByCaller = true }, CancellationToken.None);

        var list = await new GetJobIssuableMaterialsHandler(_db).Handle(
            new GetJobIssuableMaterialsQuery(job.Id), CancellationToken.None);

        list.Should().HaveCount(2);
        list[0].Should().Be(new Core.Models.JobIssuableMaterialResponseModel(
            part.Id, "P-MI-001", null, 2m, 20m, 3m, null, null));
        list[1].PartId.Should().Be(alternate.Id);
        list[1].AlternateForPartNumber.Should().Be("P-MI-001");
    }

    private static CreateMaterialIssueCommand Command(Job job, Part part, decimal quantity) => new(
        JobId: job.Id,
        PartId: part.Id,
        OperationId: null,
        Quantity: quantity,
        BinContentId: null,
        StorageLocationId: null,
        LotNumber: null,
        IssueType: MaterialIssueType.Issue,
        Notes: null,
        IssuedById: 1);

    private async Task<StorageLocation> BinAsync()
    {
        var bin = new StorageLocation { Name = "A1", LocationType = LocationType.Bin, IsActive = true };
        _db.StorageLocations.Add(bin);
        await _db.SaveChangesAsync();
        return bin;
    }

    private async Task<BinContent> StockAsync(Part part, StorageLocation bin, decimal quantity, string? lot, int daysAgo = 1)
    {
        var row = new BinContent
        {
            LocationId = bin.Id, EntityType = "part", EntityId = part.Id, Quantity = quantity, LotNumber = lot,
            PlacedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
        };
        _db.BinContents.Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    private async Task<decimal> OnHandAsync(Part part, StorageLocation bin)
        => (await _db.BinContents.AsNoTracking()
            .Where(b => b.EntityId == part.Id && b.LocationId == bin.Id && b.RemovedAt == null).ToListAsync())
            .Sum(b => b.Quantity);

    [Fact]
    public async Task Handle_NonExistentJob_ThrowsKeyNotFound()
    {
        var part = new Part { PartNumber = "P-MI-002", Name = "Test Part 2" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        var act = () => _handler.Handle(new CreateMaterialIssueCommand(
            JobId: 99999,
            PartId: part.Id,
            OperationId: null,
            Quantity: 1m,
            BinContentId: null,
            StorageLocationId: null,
            LotNumber: null,
            IssueType: MaterialIssueType.Issue,
            Notes: null,
            IssuedById: 1), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*Job 99999*");
    }

    public void Dispose() => _db.Dispose();
}
