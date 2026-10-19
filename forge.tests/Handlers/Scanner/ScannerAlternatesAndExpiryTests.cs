using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Auth;
using Forge.Api.Features.Inventory;
using Forge.Api.Features.Jobs;
using Forge.Api.Features.Scanner;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Scanner;

public class ScannerAlternatesAndExpiryTests : IDisposable
{
    private const int UserId = 7;
    private const string Pin = "2468";
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly IClock _clock = Mock.Of<IClock>(c => c.UtcNow == Now);
    private readonly IHttpContextAccessor _accessor;

    private Part _assembly = null!;
    private Part _primary = null!;
    private Part _alternate = null!;
    private StorageLocation _bin = null!;
    private int _jobId;

    public ScannerAlternatesAndExpiryTests()
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, UserId.ToString()), new Claim(ClaimTypes.Role, "Manager")], "Test")),
        });
        _accessor = accessor.Object;
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedAsync(bool approved = true, bool bidirectional = false, decimal? factor = null)
    {
        _assembly = new Part { PartNumber = "FG-320", Description = "Cover" };
        _primary = new Part { PartNumber = "RS-PA66-GF30", Description = "Resin" };
        _alternate = new Part { PartNumber = "RS-PA66-GF30-ALT", Description = "Resin alternate" };
        _bin = new StorageLocation { Name = "A1", LocationType = LocationType.Bin, IsActive = true };
        _db.AddRange(_assembly, _primary, _alternate, _bin);
        _db.Users.Add(new ApplicationUser
        {
            Id = UserId, UserName = "scanner", Email = "scanner@forge.local", FirstName = "Scan", LastName = "Ner",
            PinHash = SetPinHandler.HashPin(Pin),
        });
        await _db.SaveChangesAsync();

        _db.BOMLines.Add(new BOMLine { ParentPartId = _assembly.Id, ChildPartId = _primary.Id, Quantity = 0.02m });
        _db.PartAlternates.Add(new PartAlternate
        {
            PartId = _primary.Id, AlternatePartId = _alternate.Id, IsApproved = approved,
            IsBidirectional = bidirectional, ConversionFactor = factor,
        });
        var job = new Job { JobNumber = "J-2", Title = "Covers", TrackTypeId = 1, CurrentStageId = 1, PartId = _assembly.Id };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        _db.JobParts.Add(new JobPart { JobId = job.Id, PartId = _assembly.Id, Quantity = 1000 });
        await _db.SaveChangesAsync();
        _jobId = job.Id;
    }

    private async Task StockAsync(Part part, decimal quantity, string? lot, int daysAgo = 1)
    {
        _db.BinContents.Add(new BinContent
        {
            LocationId = _bin.Id, EntityType = "part", EntityId = part.Id, Quantity = quantity, LotNumber = lot,
            PlacedAt = Now.AddDays(-daysAgo),
        });
        await _db.SaveChangesAsync();
    }

    private async Task ExpiresAsync(Part part, string lot, DateTimeOffset expiresOn)
    {
        _db.LotRecords.Add(new LotRecord { LotNumber = lot, PartId = part.Id, Quantity = 100, ExpirationDate = expiresOn });
        await _db.SaveChangesAsync();
    }

    private Task<int> IssueAsync(Part part, decimal quantity)
    {
        _db.ChangeTracker.Clear();
        return new ExecuteScanIssueHandler(_db, _clock, _accessor, MaterialIssueMediator.For(_db)).Handle(
            new ExecuteScanIssueCommand(new ScanIssueRequestModel(part.Id, _jobId, quantity, _bin.Id)), CancellationToken.None);
    }

    [Fact]
    public async Task ApprovedAlternate_CanBeIssuedToTheJob()
    {
        await SeedAsync();
        await StockAsync(_alternate, 50m, "ALT-1");

        await IssueAsync(_alternate, 5m);

        (await _db.MaterialIssues.AsNoTracking().SingleAsync()).PartId.Should().Be(_alternate.Id);
    }

    [Fact]
    public async Task UnapprovedAlternate_IsRefused()
    {
        await SeedAsync(approved: false);
        await StockAsync(_alternate, 50m, "ALT-1");

        var act = () => IssueAsync(_alternate, 5m);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not in the BOM*");
    }

    [Fact]
    public async Task PrimaryOfAnAlternate_StandsInOnlyWhenTheRowIsBidirectional()
    {
        await SeedAsync(bidirectional: false);
        _db.BOMLines.RemoveRange(_db.BOMLines);
        _db.BOMLines.Add(new BOMLine { ParentPartId = _assembly.Id, ChildPartId = _alternate.Id, Quantity = 1m });
        await _db.SaveChangesAsync();
        await StockAsync(_primary, 50m, null);

        var refused = () => IssueAsync(_primary, 5m);
        await refused.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not in the BOM*");

        var row = await _db.PartAlternates.SingleAsync();
        row.IsBidirectional = true;
        await _db.SaveChangesAsync();

        await IssueAsync(_primary, 5m);
        (await _db.MaterialIssues.AsNoTracking().SingleAsync()).PartId.Should().Be(_primary.Id);
    }

    [Fact]
    public async Task Rules_ScaleTheAlternateByTheFactor_AndDivideItInReverse()
    {
        await SeedAsync(bidirectional: true, factor: 2m);
        _db.BOMLines.Add(new BOMLine { ParentPartId = _primary.Id, ChildPartId = _alternate.Id, Quantity = 4m });
        await _db.SaveChangesAsync();

        var forAssembly = await JobMaterialRules.ForParentAsync(_db, _assembly.Id, CancellationToken.None);
        forAssembly.Should().BeEquivalentTo([
            new IssuableComponent(_assembly.Id, _primary.Id, 0.02m, null),
            new IssuableComponent(_assembly.Id, _alternate.Id, 0.04m, _primary.Id),
        ]);

        var reverse = await JobMaterialRules.ForParentAsync(_db, _primary.Id, CancellationToken.None);
        reverse.Should().ContainEquivalentOf(new IssuableComponent(_primary.Id, _primary.Id, 2m, _alternate.Id));
    }

    [Fact]
    public async Task ScanContext_ListsTheJobForAnApprovedAlternate()
    {
        await SeedAsync(factor: 1.5m);
        await StockAsync(_alternate, 50m, "ALT-1");

        var context = await new GetScanContextHandler(_db, _accessor).Handle(
            new GetScanContextQuery(_alternate.PartNumber), CancellationToken.None);

        var issue = context.AvailableActions.Single(a => a.Action == "Issue");
        issue.Enabled.Should().BeTrue();
        var job = ((ScanIssueContext)issue.Context!).Jobs.Should().ContainSingle().Subject;
        job.JobId.Should().Be(_jobId);
        job.RequiredQuantity.Should().Be(30m);
    }

    [Fact]
    public async Task Issue_FromALocationHoldingOnlyAnExpiredLot_IsRefused()
    {
        await SeedAsync();
        await StockAsync(_primary, 50m, "R-26-119");
        await ExpiresAsync(_primary, "R-26-119", new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero));

        var act = () => IssueAsync(_primary, 5m);

        await act.Should().ThrowAsync<LotExpiredException>().WithMessage("Lot R-26-119 expired on 2026-10-10.");
        (await _db.MaterialIssues.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Issue_SkipsTheExpiredLot_AndDrawsTheGoodOne()
    {
        await SeedAsync();
        await StockAsync(_primary, 50m, "OLD", daysAgo: 9);
        await StockAsync(_primary, 50m, "GOOD", daysAgo: 1);
        await ExpiresAsync(_primary, "OLD", new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero));

        await IssueAsync(_primary, 5m);

        (await _db.MaterialIssues.AsNoTracking().SingleAsync()).LotNumber.Should().Be("GOOD");
    }

    [Fact]
    public async Task Issue_OnTheExpiryDate_IsAllowed()
    {
        await SeedAsync();
        await StockAsync(_primary, 50m, "TODAY");
        await ExpiresAsync(_primary, "TODAY", new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero));

        await IssueAsync(_primary, 5m);

        (await _db.MaterialIssues.AsNoTracking().SingleAsync()).LotNumber.Should().Be("TODAY");
    }

    [Fact]
    public void Expiry_IsAShopLocalCalendarDay()
    {
        var expiresOn = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

        LotExpiry.IsExpired(expiresOn, new DateOnly(2026, 10, 10)).Should().BeFalse();
        LotExpiry.IsExpired(expiresOn, new DateOnly(2026, 10, 11)).Should().BeTrue();
        LotExpiry.IsExpired(null, new DateOnly(2030, 1, 1)).Should().BeFalse();
    }
}
