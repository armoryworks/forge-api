using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Assets;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Assets;

public class MachineHoursAndDowntimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IClock> _clock = new();

    public MachineHoursAndDowntimeTests() => _clock.Setup(c => c.UtcNow).Returns(Now);

    [Fact]
    public async Task Recording_hours_moves_the_meter_and_logs_the_reading()
    {
        var asset = await SeedAsync(hours: 1200m);

        var result = await new UpdateMachineHoursHandler(_db, _clock.Object)
            .Handle(new UpdateMachineHoursCommand(asset.Id, new UpdateMachineHoursRequestModel(1250.5m, Now.AddDays(-1))), CancellationToken.None);

        result.CurrentHours.Should().Be(1250.5m);
        var log = await _db.ActivityLogs.SingleAsync(a => a.EntityType == "Asset" && a.EntityId == asset.Id && a.Action == "hours-recorded");
        log.Description.Should().Contain("2026-10-19");
    }

    [Fact]
    public async Task A_reading_below_the_last_one_is_refused()
    {
        var asset = await SeedAsync(hours: 1200m);

        var act = () => new UpdateMachineHoursHandler(_db, _clock.Object)
            .Handle(new UpdateMachineHoursCommand(asset.Id, new UpdateMachineHoursRequestModel(1100m)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _db.Assets.AsNoTracking().SingleAsync()).CurrentHours.Should().Be(1200m);
    }

    [Fact]
    public async Task Downtime_takes_the_assets_work_center_and_only_one_can_be_open()
    {
        var asset = await SeedAsync(hours: 0m);
        var handler = new CreateDowntimeLogHandler(_db, TestCaller.As(7, "Manager"));
        var request = new CreateDowntimeLogRequestModel(
            asset.Id, null, Now, null, DowntimeCategory.EquipmentFailure, null, "Hydraulic leak", null, null, false, null, null);

        var log = await handler.Handle(new CreateDowntimeLogCommand(request), CancellationToken.None);

        log.WorkCenterId.Should().Be(asset.WorkCenterId);
        log.Category.Should().Be(DowntimeCategory.EquipmentFailure);
        var again = () => handler.Handle(new CreateDowntimeLogCommand(request), CancellationToken.None);
        await again.Should().ThrowAsync<InvalidOperationException>();

        _clock.Setup(c => c.UtcNow).Returns(Now.AddMinutes(45));
        var ended = await new EndDowntimeLogHandler(_db, _clock.Object).Handle(new EndDowntimeLogCommand(log.Id), CancellationToken.None);
        ended.EndedAt.Should().Be(Now.AddMinutes(45));
        ended.DurationHours.Should().Be(0.75m);
        (await handler.Handle(new CreateDowntimeLogCommand(request with { StartedAt = Now.AddHours(1) }), CancellationToken.None))
            .Id.Should().NotBe(log.Id);
    }

    private async Task<Asset> SeedAsync(decimal hours)
    {
        var press = new WorkCenter { Name = "Press", Code = "PR" };
        _db.WorkCenters.Add(press);
        await _db.SaveChangesAsync();
        var asset = new Asset { Name = "Press 1", AssetType = AssetType.Machine, CurrentHours = hours, WorkCenterId = press.Id };
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();
        return asset;
    }
}
