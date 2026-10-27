using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Inventory;
using Forge.Api.Features.Quality;
using Forge.Api.Features.Scanner;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Scanner;

[Collection(PostgresCollection.Name)]
public sealed class ScanIssueSourcesPostgresTests(PostgresFixture fixture)
{
    private static readonly IClock Clock = Mock.Of<IClock>(c => c.UtcNow == DateTimeOffset.UtcNow);

    private sealed record Seeded(int UserId, int JobId, int ComponentId, int BinA, int BinB, Dictionary<string, int> Rows);

    private async Task<Seeded> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var now = DateTimeOffset.UtcNow;
        var user = new ApplicationUser
        {
            UserName = $"fe{suffix}@forge.local", Email = $"fe{suffix}@forge.local", FirstName = "Fe", LastName = "Fo", IsActive = true,
        };
        var track = new TrackType { Name = $"FE {suffix}", Code = $"fe-{suffix}", IsActive = true };
        var parent = new Part { PartNumber = $"FE-P-{suffix}", Description = "Parent" };
        var component = new Part { PartNumber = $"FE-C-{suffix}", Description = "Resin" };
        var binA = new StorageLocation { Name = $"FE-A-{suffix}", LocationType = LocationType.Bin, IsActive = true, Barcode = $"BIN-A-{suffix}" };
        var binB = new StorageLocation { Name = $"FE-B-{suffix}", LocationType = LocationType.Bin, IsActive = true };
        db.AddRange(user, track, parent, component, binA, binB);
        await db.SaveChangesAsync();

        var stage = new JobStage { TrackTypeId = track.Id, Name = "Stage 1", Code = "s1", SortOrder = 1, IsActive = true };
        db.JobStages.Add(stage);
        db.BOMLines.Add(new BOMLine { ParentPartId = parent.Id, ChildPartId = component.Id, Quantity = 1m, SortOrder = 1 });
        await db.SaveChangesAsync();

        var job = new Job
        {
            JobNumber = $"J-FE-{suffix}", Title = "FEFO job", TrackTypeId = track.Id, CurrentStageId = stage.Id, PartId = parent.Id,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        db.JobParts.Add(new JobPart { JobId = job.Id, PartId = parent.Id, Quantity = 100 });

        BinContent Row(StorageLocation at, string? lot, int placedDaysAgo, BinContentStatus status = BinContentStatus.Stored) => new()
        {
            LocationId = at.Id, EntityType = "part", EntityId = component.Id, Quantity = 20m, LotNumber = lot,
            PlacedAt = now.AddDays(-placedDaysAgo), Status = status,
        };
        var rows = new Dictionary<string, BinContent>
        {
            ["LATE"] = Row(binA, $"LATE-{suffix}", 9),
            ["SOON"] = Row(binA, $"SOON-{suffix}", 1),
            ["NODATE"] = Row(binB, $"NODATE-{suffix}", 20),
            ["LOOSE"] = Row(binB, null, 30),
            ["HELD"] = Row(binA, $"HELD-{suffix}", 40, BinContentStatus.QcHold),
            ["GONE"] = Row(binA, $"GONE-{suffix}", 50),
        };
        db.BinContents.AddRange(rows.Values);
        db.LotRecords.AddRange(
            new LotRecord { LotNumber = $"LATE-{suffix}", PartId = component.Id, Quantity = 20, ExpirationDate = now.AddDays(30) },
            new LotRecord { LotNumber = $"SOON-{suffix}", PartId = component.Id, Quantity = 20, ExpirationDate = now.AddDays(5) },
            new LotRecord { LotNumber = $"HELD-{suffix}", PartId = component.Id, Quantity = 20, ExpirationDate = now.AddDays(1) },
            new LotRecord { LotNumber = $"GONE-{suffix}", PartId = component.Id, Quantity = 20, ExpirationDate = now.AddDays(-2) });
        await db.SaveChangesAsync();

        return new Seeded(user.Id, job.Id, component.Id, binA.Id, binB.Id, rows.ToDictionary(r => r.Key, r => r.Value.Id));
    }

    private static IHttpContextAccessor Accessor(int userId)
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, "ProductionWorker")], "Test")),
        });
        return accessor.Object;
    }

    private async Task<int> IssueAsync(Seeded s, int locationId, int binContentId, decimal quantity)
    {
        await using var db = fixture.CreateContext();
        return await new ExecuteScanIssueHandler(db, Clock, Accessor(s.UserId), MaterialIssueMediator.For(db)).Handle(
            new ExecuteScanIssueCommand(new ScanIssueRequestModel(s.ComponentId, s.JobId, quantity, locationId, binContentId)),
            CancellationToken.None);
    }

    [Fact]
    public async Task Sources_AreFirstExpiringFirst_WithoutHeldOrExpiredLots()
    {
        var s = await SeedAsync();

        await using var db = fixture.CreateContext();
        var sources = await new GetScanIssueSourcesHandler(db, Clock).Handle(
            new GetScanIssueSourcesQuery(s.ComponentId), CancellationToken.None);

        sources.Select(x => x.BinContentId).Should().Equal(
            s.Rows["SOON"], s.Rows["LATE"], s.Rows["LOOSE"], s.Rows["NODATE"]);
        sources[0].ExpirationDate.Should().NotBeNull();
        sources[0].LocationBarcode.Should().StartWith("BIN-A-");
        sources[2].LotNumber.Should().BeNull();
        sources.Should().OnlyContain(x => x.AvailableQuantity == 20m);
    }

    [Fact]
    public async Task Issue_FromTheChosenLot_DrawsOnlyThatRow()
    {
        var s = await SeedAsync();

        await IssueAsync(s, s.BinA, s.Rows["LATE"], 5m);

        await using var db = fixture.CreateContext();
        var issue = await db.MaterialIssues.AsNoTracking().SingleAsync(m => m.JobId == s.JobId);
        issue.LotNumber.Should().StartWith("LATE-");
        issue.Quantity.Should().Be(5m);
        (await db.BinContents.AsNoTracking().SingleAsync(b => b.Id == s.Rows["LATE"])).Quantity.Should().Be(15m);
        (await db.BinContents.AsNoTracking().SingleAsync(b => b.Id == s.Rows["SOON"])).Quantity.Should().Be(20m);
    }

    [Fact]
    public async Task Issue_MoreThanTheChosenRowHolds_IsRefused()
    {
        var s = await SeedAsync();

        var act = () => IssueAsync(s, s.BinA, s.Rows["SOON"], 25m);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Cannot issue 25*");
    }

    [Fact]
    public async Task Issue_FromAHeldOrExpiredLot_IsRefused()
    {
        var s = await SeedAsync();

        var held = () => IssueAsync(s, s.BinA, s.Rows["HELD"], 1m);
        await held.Should().ThrowAsync<QualityHoldException>();

        var expired = () => IssueAsync(s, s.BinA, s.Rows["GONE"], 1m);
        await expired.Should().ThrowAsync<LotExpiredException>();

        var wrongBin = () => IssueAsync(s, s.BinB, s.Rows["SOON"], 1m);
        await wrongBin.Should().ThrowAsync<KeyNotFoundException>();

        await using var db = fixture.CreateContext();
        (await db.MaterialIssues.AnyAsync(m => m.JobId == s.JobId)).Should().BeFalse();
    }
}
