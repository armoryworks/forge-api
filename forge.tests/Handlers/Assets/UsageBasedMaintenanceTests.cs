using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;

using Forge.Api.Features.Assets;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Assets;

public class UsageBasedMaintenanceTests
{
    private static IHttpContextAccessor Http(int userId) => new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())])),
        },
    };

    private static async Task<Asset> SeedAssetAsync(AppDbContext db, decimal hours, int shots)
    {
        var asset = new Asset { Name = "MOLD-T112", AssetType = AssetType.Tooling, CurrentHours = hours, CurrentShotCount = shots };
        db.Assets.Add(asset);
        db.Users.Add(new ApplicationUser { Id = 7, UserName = "lena", FirstName = "Lena", LastName = "Tech" });
        await db.SaveChangesAsync();
        return asset;
    }

    [Fact]
    public async Task Create_stores_the_shot_interval()
    {
        using var db = TestDbContextFactory.Create();
        var asset = await SeedAssetAsync(db, 0m, 0);

        var result = await new CreateMaintenanceScheduleHandler(db).Handle(
            new CreateMaintenanceScheduleCommand(new CreateMaintenanceScheduleRequestModel(
                asset.Id, "Mold PM", null, 180, null, DateTimeOffset.UtcNow.AddDays(90), IntervalShots: 50_000)),
            CancellationToken.None);

        result.IntervalShots.Should().Be(50_000);
        result.IsOverdue.Should().BeFalse();
        db.MaintenanceSchedules.Single().IntervalShots.Should().Be(50_000);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(null, 0)]
    [InlineData(-5, null)]
    public void Validator_rejects_non_positive_usage_intervals(int? hours, int? shots)
    {
        var result = new CreateMaintenanceScheduleCommandValidator().Validate(new CreateMaintenanceScheduleCommand(
            new CreateMaintenanceScheduleRequestModel(1, "PM", null, 30, hours, DateTimeOffset.UtcNow, shots)));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Logging_service_records_the_asset_meters_when_none_are_given()
    {
        using var db = TestDbContextFactory.Create();
        var asset = await SeedAssetAsync(db, 1_260m, 50_500);
        var schedule = new MaintenanceSchedule { AssetId = asset.Id, Title = "Mold PM", IntervalDays = 180, IntervalShots = 50_000, NextDueAt = DateTimeOffset.UtcNow.AddDays(90) };
        db.MaintenanceSchedules.Add(schedule);
        await db.SaveChangesAsync();

        var log = await new LogMaintenanceHandler(db, Http(7)).Handle(
            new LogMaintenanceCommand(schedule.Id, new LogMaintenanceRequestModel(null, "Vents cleaned", 0m)),
            CancellationToken.None);

        log.HoursAtService.Should().Be(1_260m);
        log.ShotCountAtService.Should().Be(50_500);
    }

    [Fact]
    public async Task Schedule_list_marks_a_shot_based_schedule_overdue_until_it_is_serviced()
    {
        using var db = TestDbContextFactory.Create();
        var asset = await SeedAssetAsync(db, 0m, 50_500);
        var schedule = new MaintenanceSchedule { AssetId = asset.Id, Title = "Mold PM", IntervalDays = 180, IntervalShots = 50_000, NextDueAt = DateTimeOffset.UtcNow.AddDays(90) };
        db.MaintenanceSchedules.Add(schedule);
        await db.SaveChangesAsync();
        var list = new GetMaintenanceSchedulesHandler(db, new SystemClock());

        var before = await list.Handle(new GetMaintenanceSchedulesQuery(asset.Id), CancellationToken.None);
        before.Should().ContainSingle().Which.IsOverdue.Should().BeTrue();

        await new LogMaintenanceHandler(db, Http(7)).Handle(
            new LogMaintenanceCommand(schedule.Id, new LogMaintenanceRequestModel(null, null, null)), CancellationToken.None);

        var after = await list.Handle(new GetMaintenanceSchedulesQuery(asset.Id), CancellationToken.None);
        after.Should().ContainSingle().Which.IsOverdue.Should().BeFalse();
    }
}
