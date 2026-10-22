using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Inventory;
using Forge.Api.Features.Jobs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

/// <summary>
/// The material issue, return, alternate, expiry and backflush queries against real Postgres, where the InMemory
/// provider cannot show whether they translate or whether the return row lock runs.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MaterialIssuePostgresTests(PostgresFixture fixture)
{
    private sealed record Seeded(int UserId, int JobId, int ParentId, int ComponentId, int AlternateId, int LocationId);

    private async Task<Seeded> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var user = new ApplicationUser
        {
            UserName = $"mi{suffix}@forge.local", Email = $"mi{suffix}@forge.local", FirstName = "Mat", LastName = "Issue", IsActive = true,
        };
        var track = new TrackType { Name = $"MI {suffix}", Code = $"mi-{suffix}", IsActive = true };
        var parent = new Part { PartNumber = $"MI-P-{suffix}", Description = "Parent" };
        var component = new Part { PartNumber = $"MI-C-{suffix}", Description = "Resin", BackflushPolicy = BackflushPolicy.Auto };
        var alternate = new Part { PartNumber = $"MI-A-{suffix}", Description = "Resin alternate" };
        var location = new StorageLocation { Name = $"MI-{suffix}", LocationType = LocationType.Bin, IsActive = true };
        db.AddRange(user, track, parent, component, alternate, location);
        await db.SaveChangesAsync();

        var stage = new JobStage { TrackTypeId = track.Id, Name = "Stage 1", Code = "s1", SortOrder = 1, IsActive = true };
        db.JobStages.Add(stage);
        db.BOMLines.Add(new BOMLine { ParentPartId = parent.Id, ChildPartId = component.Id, Quantity = 0.5m, SortOrder = 1 });
        db.PartAlternates.Add(new PartAlternate
        {
            PartId = component.Id, AlternatePartId = alternate.Id, IsApproved = true, ConversionFactor = 2m,
        });
        await db.SaveChangesAsync();

        var job = new Job
        {
            JobNumber = $"J-MI-{suffix}", Title = "Material issue job", TrackTypeId = track.Id, CurrentStageId = stage.Id,
            PartId = parent.Id,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        db.JobParts.Add(new JobPart { JobId = job.Id, PartId = parent.Id, Quantity = 10 });
        db.BinContents.AddRange(
            new BinContent
            {
                LocationId = location.Id, EntityType = "part", EntityId = component.Id, Quantity = 20m, LotNumber = $"OLD-{suffix}",
                PlacedAt = DateTimeOffset.UtcNow.AddDays(-9),
            },
            new BinContent
            {
                LocationId = location.Id, EntityType = "part", EntityId = component.Id, Quantity = 20m, LotNumber = $"NEW-{suffix}",
                PlacedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
        db.LotRecords.Add(new LotRecord
        {
            LotNumber = $"OLD-{suffix}", PartId = component.Id, Quantity = 20, ExpirationDate = DateTimeOffset.UtcNow.AddDays(-3),
        });
        await db.SaveChangesAsync();
        return new Seeded(user.Id, job.Id, parent.Id, component.Id, alternate.Id, location.Id);
    }

    [Fact]
    public async Task IssueByLocation_SkipsTheExpiredLot_ReturnRestoresOnce_AndTheListFlagsIt()
    {
        var s = await SeedAsync();

        await using (var db = fixture.CreateContext())
        {
            await new CreateMaterialIssueHandler(db).Handle(new CreateMaterialIssueCommand(
                s.JobId, s.ComponentId, null, 5m, null, s.LocationId, null, MaterialIssueType.Issue, null, s.UserId),
                CancellationToken.None);
        }

        int issueId;
        await using (var db = fixture.CreateContext())
        {
            var issue = await db.MaterialIssues.SingleAsync(m => m.JobId == s.JobId);
            issue.LotNumber.Should().StartWith("NEW-");
            issueId = issue.Id;
            await new ReturnMaterialIssueHandler(db).Handle(
                new ReturnMaterialIssueCommand(s.JobId, issueId, s.UserId), CancellationToken.None);
        }

        await using (var db = fixture.CreateContext())
        {
            var again = () => new ReturnMaterialIssueHandler(db).Handle(
                new ReturnMaterialIssueCommand(s.JobId, issueId, s.UserId), CancellationToken.None);
            await again.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already been returned*");
        }

        await using (var db = fixture.CreateContext())
        {
            var onHand = await db.BinContents
                .Where(b => b.EntityId == s.ComponentId && b.LocationId == s.LocationId && b.RemovedAt == null)
                .SumAsync(b => b.Quantity);
            onHand.Should().Be(40m);
            var list = await new GetJobMaterialIssuesHandler(db).Handle(new GetJobMaterialIssuesQuery(s.JobId), CancellationToken.None);
            list.Single(m => m.Id == issueId).IsReturned.Should().BeTrue();
        }
    }

    [Fact]
    public async Task IssuableMaterials_AndTheAlternateLookup_Translate()
    {
        var s = await SeedAsync();
        await using var db = fixture.CreateContext();

        var list = await new GetJobIssuableMaterialsHandler(db).Handle(
            new GetJobIssuableMaterialsQuery(s.JobId), CancellationToken.None);
        var forAlternate = await JobMaterialRules.ForIssuedPartAsync(db, s.AlternateId, CancellationToken.None);

        list.Select(m => (m.PartId, m.RequiredQuantity)).Should().Equal((s.ComponentId, 5m), (s.AlternateId, 10m));
        forAlternate.Should().ContainSingle().Which.Should().Be(new IssuableComponent(s.ParentId, s.AlternateId, 1m, s.ComponentId));
    }

    private static BackflushService Backflush(AppDbContext db)
    {
        var capabilities = new Mock<ICapabilitySnapshotProvider>();
        capabilities.Setup(c => c.IsEnabled(BackflushService.Capability)).Returns(true);
        var settings = new Mock<ISettingsService>();
        settings.Setup(x => x.GetStringAsync(ShopFloorSettings.BackflushDefaultKey, It.IsAny<CancellationToken>())).ReturnsAsync("Manual");
        return new BackflushService(db, capabilities.Object, settings.Object, new UomService(db), MaterialIssueMediator.For(db));
    }

    private async Task ReceivedRunAsync(Seeded s, int good, int scrap)
    {
        await using var db = fixture.CreateContext();
        db.ProductionRuns.Add(new ProductionRun
        {
            JobId = s.JobId, PartId = s.ParentId, RunNumber = $"RUN-{Guid.NewGuid():N}"[..20], TargetQuantity = good + scrap,
            CompletedQuantity = good, ScrapQuantity = scrap, Status = ProductionRunStatus.Completed,
            ReceivedToStockAt = DateTimeOffset.UtcNow, ReceivedQuantity = good,
        });
        await db.SaveChangesAsync();
    }

    private async Task<decimal> BackflushedAsync(Seeded s)
    {
        await using var db = fixture.CreateContext();
        return await db.MaterialIssues
            .Where(m => m.JobId == s.JobId && m.Notes != null && m.Notes.StartsWith(BackflushService.NotePrefix))
            .SumAsync(m => m.Quantity);
    }

    [Fact]
    public async Task Backflush_NetsHandIssuesReturnsAndTheAlternate()
    {
        var s = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            await new CreateMaterialIssueHandler(db).Handle(new CreateMaterialIssueCommand(
                s.JobId, s.ComponentId, null, 1m, null, s.LocationId, null, MaterialIssueType.Issue, null, s.UserId),
                CancellationToken.None);
            db.MaterialIssues.AddRange(
                new MaterialIssue { JobId = s.JobId, PartId = s.AlternateId, Quantity = 4m, IssueType = MaterialIssueType.Issue, IssuedById = s.UserId, IssuedAt = DateTimeOffset.UtcNow },
                new MaterialIssue { JobId = s.JobId, PartId = s.AlternateId, Quantity = 2m, IssueType = MaterialIssueType.Return, IssuedById = s.UserId, IssuedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        await ReceivedRunAsync(s, good: 8, scrap: 2);

        await using (var db = fixture.CreateContext())
            await Backflush(db).BackflushJobAsync(s.JobId, s.UserId, DateTimeOffset.UtcNow, CancellationToken.None);

        (await BackflushedAsync(s)).Should().Be(3m);
    }

    [Fact]
    public async Task SimultaneousBackflushes_OfOneJob_ConsumeTheRequirementOnce()
    {
        var s = await SeedAsync();
        await ReceivedRunAsync(s, good: 8, scrap: 2);

        async Task RunAsync()
        {
            await using var db = fixture.CreateContext();
            await using var tx = await db.Database.BeginTransactionAsync();
            db.JobActivityLogs.Add(new JobActivityLog
            {
                JobId = s.JobId, UserId = s.UserId, Action = ActivityAction.StatusChanged, Description = "Received", CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            await Backflush(db).BackflushJobAsync(s.JobId, s.UserId, DateTimeOffset.UtcNow, CancellationToken.None);
            await tx.CommitAsync();
        }

        await Task.WhenAll(RunAsync(), RunAsync(), RunAsync());

        (await BackflushedAsync(s)).Should().Be(5m);
    }

    [Fact]
    public async Task Backflush_ConsumesOnceForReceivedOutput()
    {
        var s = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            db.ProductionRuns.Add(new ProductionRun
            {
                JobId = s.JobId, PartId = s.ParentId, RunNumber = $"RUN-{Guid.NewGuid():N}"[..20], TargetQuantity = 10,
                CompletedQuantity = 8, ScrapQuantity = 2, Status = ProductionRunStatus.Completed,
                ReceivedToStockAt = DateTimeOffset.UtcNow, ReceivedQuantity = 8,
            });
            await db.SaveChangesAsync();
        }

        var capabilities = new Mock<ICapabilitySnapshotProvider>();
        capabilities.Setup(c => c.IsEnabled(BackflushService.Capability)).Returns(true);
        var settings = new Mock<ISettingsService>();
        settings.Setup(x => x.GetStringAsync(ShopFloorSettings.BackflushDefaultKey, It.IsAny<CancellationToken>())).ReturnsAsync("Manual");

        for (var i = 0; i < 2; i++)
        {
            await using var db = fixture.CreateContext();
            await new BackflushService(db, capabilities.Object, settings.Object, new UomService(db), MaterialIssueMediator.For(db))
                .BackflushJobAsync(s.JobId, s.UserId, DateTimeOffset.UtcNow, CancellationToken.None);
        }

        await using (var db = fixture.CreateContext())
        {
            var issues = await db.MaterialIssues.Where(m => m.JobId == s.JobId).ToListAsync();
            issues.Sum(m => m.Quantity).Should().Be(5m);
            issues.Should().OnlyContain(m => m.LotNumber!.StartsWith("NEW-"));
        }
    }
}
