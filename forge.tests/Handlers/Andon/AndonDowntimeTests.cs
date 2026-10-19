using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Andon;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Andon;

public class AndonDowntimeTests
{
    private static readonly DateTimeOffset RaisedAt = new(2026, 10, 8, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ResolvedAt = RaisedAt.AddMinutes(45);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IClock> _clock = new();
    private readonly Mock<IHttpContextAccessor> _httpContext = new();
    private readonly Mock<IMediator> _alertsReader = new();

    public AndonDowntimeTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(RaisedAt);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "7")], "Test"));
        _httpContext.Setup(h => h.HttpContext).Returns(new DefaultHttpContext { User = principal });
        _alertsReader
            .Setup(m => m.Send(It.IsAny<GetAndonAlertsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _db.AndonAlerts.AsNoTracking()
                .Select(a => new AndonAlertResponseModel { Id = a.Id, WorkCenterId = a.WorkCenterId, Type = a.Type, Status = a.Status })
                .ToList());
    }

    [Fact]
    public async Task Stoppage_OpensAnUnplannedDowntimeLogOnTheWorkCentersMachine()
    {
        var press = await AddWorkCenterAsync("Press");
        var mold = await AddAssetAsync("Mold 4", AssetType.Tooling, press.Id);
        var machine = await AddAssetAsync("Press 220", AssetType.Machine, press.Id);

        await RaiseAsync(press.Id, AndonAlertType.Stoppage);

        var log = await _db.DowntimeLogs.AsNoTracking().SingleAsync();
        log.AssetId.Should().Be(machine.Id).And.NotBe(mold.Id);
        log.WorkCenterId.Should().Be(press.Id);
        log.IsPlanned.Should().BeFalse();
        log.StartedAt.Should().Be(RaisedAt);
        log.EndedAt.Should().BeNull();
    }

    [Fact]
    public async Task Stoppage_PrefersTheAssetLinkedOnTheWorkCenter()
    {
        var press = await AddWorkCenterAsync("Press");
        await AddAssetAsync("Press A", AssetType.Machine, press.Id);
        var linked = await AddAssetAsync("Press B", AssetType.Machine, null);
        press.AssetId = linked.Id;
        await _db.SaveChangesAsync();

        await RaiseAsync(press.Id, AndonAlertType.Stoppage);

        (await _db.DowntimeLogs.AsNoTracking().SingleAsync()).AssetId.Should().Be(linked.Id);
    }

    [Theory]
    [InlineData(AndonAlertType.Material)]
    [InlineData(AndonAlertType.Quality)]
    [InlineData(AndonAlertType.Help)]
    public async Task OtherAlerts_LogNoDowntime(AndonAlertType type)
    {
        var press = await AddWorkCenterAsync("Press");
        await AddAssetAsync("Press 220", AssetType.Machine, press.Id);

        await RaiseAsync(press.Id, type);

        (await _db.DowntimeLogs.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Stoppage_OnAWorkCenterWithoutAMachine_StillRaisesTheAlert()
    {
        var bench = await AddWorkCenterAsync("Bench");

        await RaiseAsync(bench.Id, AndonAlertType.Stoppage);

        (await _db.AndonAlerts.CountAsync()).Should().Be(1);
        (await _db.DowntimeLogs.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ResolvingTheStoppage_ClosesItsDowntimeLog()
    {
        var press = await AddWorkCenterAsync("Press");
        await AddAssetAsync("Press 220", AssetType.Machine, press.Id);
        var alert = await RaiseAsync(press.Id, AndonAlertType.Stoppage);

        _clock.Setup(c => c.UtcNow).Returns(ResolvedAt);
        await new ResolveAndonAlertHandler(_db, _clock.Object, _httpContext.Object)
            .Handle(new ResolveAndonAlertCommand(alert.Id, null), CancellationToken.None);

        var log = await _db.DowntimeLogs.AsNoTracking().SingleAsync();
        log.EndedAt.Should().Be(ResolvedAt);
        log.DurationMinutes.Should().Be(45m);
    }

    private async Task<AndonAlertResponseModel> RaiseAsync(int workCenterId, AndonAlertType type) =>
        await new CreateAndonAlertHandler(
                _db, _clock.Object, _httpContext.Object, _alertsReader.Object, NullLogger<CreateAndonAlertHandler>.Instance)
            .Handle(new CreateAndonAlertCommand(new CreateAndonAlertRequestModel { WorkCenterId = workCenterId, Type = type }),
                CancellationToken.None);

    private async Task<WorkCenter> AddWorkCenterAsync(string name)
    {
        var workCenter = new WorkCenter { Name = name, Code = name.ToUpperInvariant() };
        _db.WorkCenters.Add(workCenter);
        await _db.SaveChangesAsync();
        return workCenter;
    }

    private async Task<Asset> AddAssetAsync(string name, AssetType type, int? workCenterId)
    {
        var asset = new Asset { Name = name, AssetType = type, WorkCenterId = workCenterId };
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();
        return asset;
    }
}
