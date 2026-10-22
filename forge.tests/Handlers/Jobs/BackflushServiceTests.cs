using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Jobs.ProductionRuns;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Data.Services;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class BackflushServiceTests : IDisposable
{
    private const int UserId = 1;
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 15, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<ICapabilitySnapshotProvider> _capabilities = new();
    private readonly Mock<ISettingsService> _settings = new();
    private string _plantDefault = "Manual";

    private Part _parent = null!;
    private Job _job = null!;
    private StorageLocation _bin = null!;
    private StorageLocation _otherBin = null!;

    public BackflushServiceTests()
    {
        _capabilities.Setup(c => c.IsEnabled(BackflushService.Capability)).Returns(true);
        _settings.Setup(s => s.GetStringAsync(ShopFloorSettings.BackflushDefaultKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _plantDefault);
    }

    public void Dispose() => _db.Dispose();

    private BackflushService Service()
        => new(_db, _capabilities.Object, _settings.Object, new UomService(_db), MaterialIssueMediator.For(_db));

    private Task BackflushAsync() => Service().BackflushJobAsync(_job.Id, UserId, Now, CancellationToken.None);

    private async Task SeedAsync()
    {
        _parent = new Part { PartNumber = "FG-310", Name = "Housing" };
        _bin = new StorageLocation { Name = "RES-1", LocationType = LocationType.Bin, IsActive = true };
        _otherBin = new StorageLocation { Name = "RES-2", LocationType = LocationType.Bin, IsActive = true };
        _db.AddRange(_parent, _bin, _otherBin);
        await _db.SaveChangesAsync();
        _job = new Job { JobNumber = "J-3", Title = "Housings", TrackTypeId = 1, CurrentStageId = 1, PartId = _parent.Id };
        _db.Jobs.Add(_job);
        await _db.SaveChangesAsync();
    }

    private async Task<Part> ComponentAsync(string number, decimal perUnit, BackflushPolicy? policy, int? uomId = null, int? stockUomId = null)
    {
        var part = new Part { PartNumber = number, Name = number, BackflushPolicy = policy, StockUomId = stockUomId, DefaultBinId = _bin.Id };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        _db.BOMLines.Add(new BOMLine { ParentPartId = _parent.Id, ChildPartId = part.Id, Quantity = perUnit, UomId = uomId });
        await _db.SaveChangesAsync();
        return part;
    }

    private async Task<BinContent> StockAsync(Part part, StorageLocation location, decimal quantity, string? lot,
        int daysAgo = 1, BinContentStatus status = BinContentStatus.Stored)
    {
        var row = new BinContent
        {
            LocationId = location.Id, EntityType = "part", EntityId = part.Id, Quantity = quantity, LotNumber = lot,
            Status = status, PlacedAt = Now.AddDays(-daysAgo),
        };
        _db.BinContents.Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    private async Task<ProductionRun> ReceivedRunAsync(int good, int scrap)
    {
        var run = new ProductionRun
        {
            JobId = _job.Id, PartId = _parent.Id, RunNumber = $"RUN-{Guid.NewGuid():N}", TargetQuantity = good + scrap,
            CompletedQuantity = good, ScrapQuantity = scrap, Status = ProductionRunStatus.Completed,
            ReceivedToStockAt = Now, ReceivedQuantity = good,
        };
        _db.ProductionRuns.Add(run);
        await _db.SaveChangesAsync();
        return run;
    }

    private async Task HandIssueAsync(Part part, decimal quantity, MaterialIssueType type = MaterialIssueType.Issue)
    {
        _db.MaterialIssues.Add(new MaterialIssue
        {
            JobId = _job.Id, PartId = part.Id, Quantity = quantity, IssueType = type, IssuedById = UserId, IssuedAt = Now,
        });
        await _db.SaveChangesAsync();
    }

    private async Task<Part> AlternateAsync(Part primary, string number, decimal? factor, bool approved = true)
    {
        var part = new Part { PartNumber = number, Name = number, DefaultBinId = _bin.Id };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        _db.PartAlternates.Add(new PartAlternate
        {
            PartId = primary.Id, AlternatePartId = part.Id, ConversionFactor = factor, IsApproved = approved,
        });
        await _db.SaveChangesAsync();
        return part;
    }

    private Task<List<MaterialIssue>> BackflushIssuesAsync(Part part)
        => _db.MaterialIssues.AsNoTracking()
            .Where(m => m.PartId == part.Id && m.Notes != null && m.Notes.StartsWith(BackflushService.NotePrefix))
            .OrderBy(m => m.Id).ToListAsync();

    private Task<List<MaterialIssue>> IssuesAsync(Part part)
        => _db.MaterialIssues.AsNoTracking().Where(m => m.PartId == part.Id).OrderBy(m => m.Id).ToListAsync();

    private async Task<decimal> OnHandAsync(Part part)
        => (await _db.BinContents.AsNoTracking()
            .Where(b => b.EntityId == part.Id && b.RemovedAt == null).ToListAsync()).Sum(b => b.Quantity);

    [Fact]
    public async Task ReceivedRun_ConsumesBomQuantityTimesGoodPlusScrap_OldestLotFirst()
    {
        await SeedAsync();
        var resin = await ComponentAsync("RS-PCABS-BLK", 0.18m, BackflushPolicy.Auto);
        await StockAsync(resin, _bin, 300m, "R-1", daysAgo: 5);
        await StockAsync(resin, _bin, 250m, "R-2", daysAgo: 2);
        await ReceivedRunAsync(good: 1940, scrap: 60);

        await BackflushAsync();

        var issues = await IssuesAsync(resin);
        issues.Select(i => (i.LotNumber, i.Quantity, i.IssueType, i.StorageLocationId)).Should().Equal(
            ("R-1", 300m, MaterialIssueType.Issue, (int?)_bin.Id),
            ("R-2", 60m, MaterialIssueType.Issue, (int?)_bin.Id));
        issues.Should().OnlyContain(i => i.BinContentId != null && i.Notes!.StartsWith(BackflushService.NotePrefix));
        (await OnHandAsync(resin)).Should().Be(190m);
    }

    [Fact]
    public async Task RepeatCalls_ConsumeOnlyNewOutput()
    {
        await SeedAsync();
        var resin = await ComponentAsync("RS-1", 0.5m, BackflushPolicy.Auto);
        await StockAsync(resin, _bin, 100m, "R-1");
        await ReceivedRunAsync(good: 40, scrap: 0);

        await BackflushAsync();
        await BackflushAsync();
        (await IssuesAsync(resin)).Sum(i => i.Quantity).Should().Be(20m);

        await ReceivedRunAsync(good: 10, scrap: 2);
        await BackflushAsync();

        (await IssuesAsync(resin)).Sum(i => i.Quantity).Should().Be(26m);
        (await OnHandAsync(resin)).Should().Be(74m);
    }

    [Fact]
    public async Task RunAndCompletedLastStep_ReportingTheSameOutput_ConsumeOnce()
    {
        await SeedAsync();
        var resin = await ComponentAsync("RS-1", 1m, BackflushPolicy.Auto);
        await StockAsync(resin, _bin, 100m, null);
        var step = new Operation { PartId = _parent.Id, StepNumber = 10, Title = "Mold" };
        _db.Operations.Add(step);
        await _db.SaveChangesAsync();
        _db.JobOperations.Add(new JobOperation
        {
            JobId = _job.Id, OperationId = step.Id, StepNumber = 10, Title = "Mold",
            Status = JobOperationStatus.Complete, CompletedQuantity = 30m,
        });
        await _db.SaveChangesAsync();

        await BackflushAsync();
        await ReceivedRunAsync(good: 30, scrap: 0);
        await BackflushAsync();

        (await IssuesAsync(resin)).Sum(i => i.Quantity).Should().Be(30m);
    }

    [Fact]
    public async Task RequirementIssuedByHand_BackflushConsumesNothing()
    {
        await SeedAsync();
        var resin = await ComponentAsync("RS-PCABS-BLK", 0.18m, BackflushPolicy.Auto);
        await StockAsync(resin, _bin, 190m, "R-26-117");
        await HandIssueAsync(resin, 360m);
        await ReceivedRunAsync(good: 1940, scrap: 60);

        await BackflushAsync();

        (await BackflushIssuesAsync(resin)).Should().BeEmpty();
        (await OnHandAsync(resin)).Should().Be(190m);
    }

    [Fact]
    public async Task PartialHandIssue_BackflushConsumesOnlyTheRemainder()
    {
        await SeedAsync();
        var resin = await ComponentAsync("RS-1", 0.5m, BackflushPolicy.Auto);
        await StockAsync(resin, _bin, 100m, "R-1");
        await HandIssueAsync(resin, 12m);
        await ReceivedRunAsync(good: 40, scrap: 0);

        await BackflushAsync();

        (await BackflushIssuesAsync(resin)).Select(i => i.Quantity).Should().Equal(8m);
        (await OnHandAsync(resin)).Should().Be(92m);
    }

    [Fact]
    public async Task ReturnedHandIssues_NoLongerCount_SoBackflushConsumesThem()
    {
        await SeedAsync();
        var resin = await ComponentAsync("RS-1", 0.5m, BackflushPolicy.Auto);
        await StockAsync(resin, _bin, 100m, "R-1");
        await HandIssueAsync(resin, 20m);
        await HandIssueAsync(resin, 6m, MaterialIssueType.Return);
        await HandIssueAsync(resin, 3m, MaterialIssueType.Scrap);
        await ReceivedRunAsync(good: 40, scrap: 0);

        await BackflushAsync();

        (await BackflushIssuesAsync(resin)).Select(i => i.Quantity).Should().Equal(6m);
    }

    [Fact]
    public async Task OverIssuedByHand_BackflushConsumesNothing_AndNeverGivesStockBack()
    {
        await SeedAsync();
        var resin = await ComponentAsync("RS-1", 0.5m, BackflushPolicy.Auto);
        await StockAsync(resin, _bin, 100m, "R-1");
        await HandIssueAsync(resin, 30m);
        await ReceivedRunAsync(good: 40, scrap: 0);

        await BackflushAsync();

        (await IssuesAsync(resin)).Should().ContainSingle().Which.IssueType.Should().Be(MaterialIssueType.Issue);
        (await OnHandAsync(resin)).Should().Be(100m);

        await ReceivedRunAsync(good: 30, scrap: 0);
        await BackflushAsync();

        (await BackflushIssuesAsync(resin)).Select(i => i.Quantity).Should().Equal(5m);
        (await OnHandAsync(resin)).Should().Be(95m);
    }

    [Fact]
    public async Task SeveralReports_WithHandIssuesBetween_KeepTheHighWaterMark()
    {
        await SeedAsync();
        var resin = await ComponentAsync("RS-1", 0.5m, BackflushPolicy.Auto);
        await StockAsync(resin, _bin, 100m, "R-1");
        var step = new Operation { PartId = _parent.Id, StepNumber = 10, Title = "Mold" };
        _db.Operations.Add(step);
        await _db.SaveChangesAsync();
        var lastStep = new JobOperation
        {
            JobId = _job.Id, OperationId = step.Id, StepNumber = 10, Title = "Mold",
            Status = JobOperationStatus.Complete, CompletedQuantity = 40m,
        };
        _db.JobOperations.Add(lastStep);
        await _db.SaveChangesAsync();

        await BackflushAsync();
        await HandIssueAsync(resin, 5m);
        await ReceivedRunAsync(good: 40, scrap: 0);
        await BackflushAsync();
        (await BackflushIssuesAsync(resin)).Select(i => i.Quantity).Should().Equal(20m);

        await ReceivedRunAsync(good: 10, scrap: 0);
        await BackflushAsync();
        (await BackflushIssuesAsync(resin)).Select(i => i.Quantity).Should().Equal(20m);

        await ReceivedRunAsync(good: 10, scrap: 0);
        await BackflushAsync();
        await BackflushAsync();
        (await BackflushIssuesAsync(resin)).Select(i => i.Quantity).Should().Equal(20m, 5m);
        (await OnHandAsync(resin)).Should().Be(75m);
    }

    [Fact]
    public async Task AlternateIssuedByHand_NetsItsPrimary_ThroughTheConversionFactor()
    {
        await SeedAsync();
        var resin = await ComponentAsync("RS-1", 0.5m, BackflushPolicy.Auto);
        var alternate = await AlternateAsync(resin, "RS-1-ALT", factor: 2m);
        await StockAsync(resin, _bin, 100m, "R-1");
        await HandIssueAsync(alternate, 4m);
        await ReceivedRunAsync(good: 10, scrap: 0);

        await BackflushAsync();

        (await BackflushIssuesAsync(resin)).Select(i => i.Quantity).Should().Equal(3m);
        (await BackflushIssuesAsync(alternate)).Should().BeEmpty();
    }

    [Fact]
    public async Task BidirectionalAlternate_IssuedByHand_DividesByTheFactor_AndAnUnapprovedOneDoesNotCount()
    {
        await SeedAsync();
        var resin = await ComponentAsync("RS-1", 0.5m, BackflushPolicy.Auto);
        var standIn = new Part { PartNumber = "RS-1-BULK", Name = "RS-1-BULK", DefaultBinId = _bin.Id };
        var unapproved = new Part { PartNumber = "RS-1-NEW", Name = "RS-1-NEW", DefaultBinId = _bin.Id };
        _db.Parts.AddRange(standIn, unapproved);
        await _db.SaveChangesAsync();
        _db.PartAlternates.AddRange(
            new PartAlternate { PartId = standIn.Id, AlternatePartId = resin.Id, ConversionFactor = 4m, IsApproved = true, IsBidirectional = true },
            new PartAlternate { PartId = resin.Id, AlternatePartId = unapproved.Id, ConversionFactor = 1m, IsApproved = false });
        await _db.SaveChangesAsync();
        await StockAsync(resin, _bin, 100m, "R-1");
        await HandIssueAsync(standIn, 0.25m);
        await HandIssueAsync(unapproved, 2m);
        await ReceivedRunAsync(good: 10, scrap: 0);

        await BackflushAsync();

        (await BackflushIssuesAsync(resin)).Select(i => i.Quantity).Should().Equal(4m);
    }

    [Fact]
    public async Task AlternateOfAComponentOnSeveralBomLines_CoversEveryLine()
    {
        await SeedAsync();
        var resin = await ComponentAsync("RS-1", 0.25m, BackflushPolicy.Auto);
        _db.BOMLines.Add(new BOMLine { ParentPartId = _parent.Id, ChildPartId = resin.Id, Quantity = 0.25m });
        await _db.SaveChangesAsync();
        var alternate = await AlternateAsync(resin, "RS-1-ALT", factor: 2m);
        await StockAsync(resin, _bin, 100m, "R-1");
        await HandIssueAsync(alternate, 10m);
        await ReceivedRunAsync(good: 10, scrap: 0);

        await BackflushAsync();

        (await BackflushIssuesAsync(resin)).Should().BeEmpty();
        (await OnHandAsync(resin)).Should().Be(100m);
    }

    [Fact]
    public async Task AlternateIssuedByHand_InAnotherUnit_NetsTheConvertedRequirement()
    {
        await SeedAsync();
        var grams = new UnitOfMeasure { Code = "G", Name = "Gram", Category = UomCategory.Weight };
        var kilos = new UnitOfMeasure { Code = "KG", Name = "Kilogram", Category = UomCategory.Weight };
        _db.AddRange(grams, kilos);
        await _db.SaveChangesAsync();
        _db.Add(new UomConversion { FromUomId = grams.Id, ToUomId = kilos.Id, ConversionFactor = 0.001m });
        await _db.SaveChangesAsync();
        var resin = await ComponentAsync("RS-1", 250m, BackflushPolicy.Auto, uomId: grams.Id, stockUomId: kilos.Id);
        var alternate = await AlternateAsync(resin, "RS-1-ALT", factor: 2m);
        await StockAsync(resin, _bin, 10m, null);
        await HandIssueAsync(alternate, 1000m);
        await ReceivedRunAsync(good: 8, scrap: 0);

        await BackflushAsync();

        (await BackflushIssuesAsync(resin)).Select(i => i.Quantity).Should().Equal(1.5m);
    }

    [Fact]
    public async Task LastStepNotComplete_ConsumesNothing()
    {
        await SeedAsync();
        var resin = await ComponentAsync("RS-1", 1m, BackflushPolicy.Auto);
        await StockAsync(resin, _bin, 100m, null);
        var step = new Operation { PartId = _parent.Id, StepNumber = 10, Title = "Mold" };
        _db.Operations.Add(step);
        await _db.SaveChangesAsync();
        _db.JobOperations.Add(new JobOperation
        {
            JobId = _job.Id, OperationId = step.Id, StepNumber = 10, Title = "Mold",
            Status = JobOperationStatus.InProgress, CompletedQuantity = 30m,
        });
        await _db.SaveChangesAsync();

        await BackflushAsync();

        (await IssuesAsync(resin)).Should().BeEmpty();
    }

    [Fact]
    public async Task ManualNoneAndDefault_FollowThePolicyAndThePlantSetting()
    {
        await SeedAsync();
        var manual = await ComponentAsync("C-MAN", 1m, BackflushPolicy.Manual);
        var none = await ComponentAsync("C-NONE", 1m, BackflushPolicy.None);
        var byDefault = await ComponentAsync("C-DEF", 1m, null);
        foreach (var part in new[] { manual, none, byDefault })
            await StockAsync(part, _bin, 50m, null);
        await ReceivedRunAsync(good: 5, scrap: 0);

        await BackflushAsync();
        (await _db.MaterialIssues.AnyAsync()).Should().BeFalse();

        _plantDefault = "Auto";
        await BackflushAsync();

        (await IssuesAsync(byDefault)).Sum(i => i.Quantity).Should().Be(5m);
        (await IssuesAsync(manual)).Should().BeEmpty();
        (await IssuesAsync(none)).Should().BeEmpty();
    }

    [Fact]
    public async Task CapabilityOff_ConsumesNothing()
    {
        await SeedAsync();
        var resin = await ComponentAsync("RS-1", 1m, BackflushPolicy.Auto);
        await StockAsync(resin, _bin, 100m, null);
        await ReceivedRunAsync(good: 5, scrap: 0);
        _capabilities.Setup(c => c.IsEnabled(BackflushService.Capability)).Returns(false);

        await BackflushAsync();

        (await _db.MaterialIssues.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task HeldAndExpiredLots_AreSkipped_AndTheOtherBinIsUsed()
    {
        await SeedAsync();
        var resin = await ComponentAsync("RS-1", 1m, BackflushPolicy.Auto);
        await StockAsync(resin, _bin, 50m, "HELD", daysAgo: 9, status: BinContentStatus.QcHold);
        await StockAsync(resin, _bin, 50m, "OLD", daysAgo: 8);
        _db.LotRecords.Add(new LotRecord { LotNumber = "OLD", PartId = resin.Id, Quantity = 50, ExpirationDate = Now.AddDays(-2) });
        await StockAsync(resin, _otherBin, 50m, "GOOD", daysAgo: 1);
        await ReceivedRunAsync(good: 10, scrap: 0);

        await BackflushAsync();

        (await IssuesAsync(resin)).Select(i => (i.LotNumber, i.Quantity, i.StorageLocationId))
            .Should().Equal(("GOOD", 10m, (int?)_otherBin.Id));
    }

    [Fact]
    public async Task Shortage_IssuesWhatIsThere_LogsTheShortfall_AndCatchesUpAfterRestock()
    {
        await SeedAsync();
        var resin = await ComponentAsync("RS-1", 1m, BackflushPolicy.Auto);
        await StockAsync(resin, _bin, 4m, null);
        await ReceivedRunAsync(good: 10, scrap: 0);

        await BackflushAsync();

        (await IssuesAsync(resin)).Sum(i => i.Quantity).Should().Be(4m);
        (await _db.JobActivityLogs.AsNoTracking().SingleAsync(l => l.FieldName == "Backflush"))
            .Description.Should().Contain("RS-1").And.Contain("6");

        await StockAsync(resin, _otherBin, 20m, null);
        await BackflushAsync();
        (await IssuesAsync(resin)).Sum(i => i.Quantity).Should().Be(10m);
    }

    [Fact]
    public async Task BomUnit_IsConvertedToTheStockUnit()
    {
        await SeedAsync();
        var grams = new UnitOfMeasure { Code = "G", Name = "Gram", Category = UomCategory.Weight };
        var kilos = new UnitOfMeasure { Code = "KG", Name = "Kilogram", Category = UomCategory.Weight };
        _db.AddRange(grams, kilos);
        await _db.SaveChangesAsync();
        _db.Add(new UomConversion { FromUomId = grams.Id, ToUomId = kilos.Id, ConversionFactor = 0.001m });
        await _db.SaveChangesAsync();
        var resin = await ComponentAsync("RS-1", 250m, BackflushPolicy.Auto, uomId: grams.Id, stockUomId: kilos.Id);
        await StockAsync(resin, _bin, 10m, null);
        await ReceivedRunAsync(good: 8, scrap: 0);

        await BackflushAsync();

        (await IssuesAsync(resin)).Sum(i => i.Quantity).Should().Be(2m);
    }

    [Fact]
    public async Task ReceivingARun_BackflushesItsComponents()
    {
        await SeedAsync();
        var resin = await ComponentAsync("RS-1", 0.5m, BackflushPolicy.Auto);
        await StockAsync(resin, _bin, 100m, null);
        var run = new ProductionRun
        {
            JobId = _job.Id, PartId = _parent.Id, RunNumber = "RUN-1", TargetQuantity = 10,
            CompletedQuantity = 8, ScrapQuantity = 2, Status = ProductionRunStatus.Completed, CompletedAt = Now,
        };
        _db.ProductionRuns.Add(run);
        await _db.SaveChangesAsync();
        _capabilities.Setup(c => c.IsEnabled(ReceiveProductionRunToStockHandler.MultiLocationCapability)).Returns(true);
        var handler = new ReceiveProductionRunToStockHandler(
            _db, new SystemClock(), _capabilities.Object, new InventoryRepository(_db), posting: null, backflush: Service());

        await handler.Handle(new ReceiveProductionRunToStockCommand(_job.Id, run.Id, UserId), CancellationToken.None);

        (await IssuesAsync(resin)).Sum(i => i.Quantity).Should().Be(5m);
    }
}
