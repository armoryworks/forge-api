using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Notifications;
using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Jobs;

public class OverdueMaintenanceJobTests
{
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow.AddMinutes(5);

    private static async Task<(Asset Asset, MaintenanceSchedule Schedule)> SeedScheduleAsync(
        AppDbContext db, decimal currentHours = 0m, int currentShots = 0,
        decimal? intervalHours = null, int? intervalShots = null, DateTimeOffset? nextDueAt = null)
    {
        var asset = new Asset { Name = "VMC-01", AssetType = AssetType.Machine, CurrentHours = currentHours, CurrentShotCount = currentShots };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        var schedule = new MaintenanceSchedule
        {
            AssetId = asset.Id,
            Title = "Spindle lube",
            IntervalDays = 90,
            IntervalHours = intervalHours,
            IntervalShots = intervalShots,
            NextDueAt = nextDueAt ?? Now.AddDays(60),
        };
        db.MaintenanceSchedules.Add(schedule);
        await db.SaveChangesAsync();
        return (asset, schedule);
    }

    private static (Mock<IMediator> Mediator, List<CreateNotificationRequestModel> Sent) CapturingMediator()
    {
        var sent = new List<CreateNotificationRequestModel>();
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationResponseModel>, CancellationToken>(
                (cmd, _) => sent.Add(((CreateNotificationCommand)cmd).Data))
            .ReturnsAsync((NotificationResponseModel)null!);
        return (mediator, sent);
    }

    private static OverdueMaintenanceJob BuildJob(AppDbContext db, Mock<IMediator> mediator)
    {
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(x => x.GetUsersInRoleAsync("Admin")).ReturnsAsync([new ApplicationUser { Id = 7 }]);
        userManager.Setup(x => x.GetUsersInRoleAsync("Manager")).ReturnsAsync([]);

        var capabilities = new Mock<ICapabilitySnapshotProvider>();
        capabilities.Setup(c => c.IsEnabled("CAP-MAINT-PM")).Returns(true);

        return new OverdueMaintenanceJob(
            db, userManager.Object, mediator.Object, new FixedClock(Now),
            NullLogger<OverdueMaintenanceJob>.Instance, capabilities.Object);
    }

    [Fact]
    public async Task HourBasedSchedule_PastInterval_Notifies()
    {
        using var db = TestDbContextFactory.Create();
        await SeedScheduleAsync(db, currentHours: 260m, intervalHours: 250m);
        var (mediator, sent) = CapturingMediator();

        await BuildJob(db, mediator).CheckOverdueMaintenanceAsync();

        sent.Should().ContainSingle();
        sent[0].Title.Should().Be("Overdue Maintenance: Spindle lube");
        sent[0].Message.Should().Contain("260 hours since last service (interval 250)");
    }

    [Fact]
    public async Task HourBasedSchedule_BelowIntervalSinceLastService_DoesNotNotify()
    {
        using var db = TestDbContextFactory.Create();
        var (_, schedule) = await SeedScheduleAsync(db, currentHours: 400m, intervalHours: 250m);
        db.MaintenanceLogs.Add(new MaintenanceLog { MaintenanceScheduleId = schedule.Id, PerformedById = 7, PerformedAt = Now.AddDays(-3), HoursAtService = 300m });
        await db.SaveChangesAsync();
        var (mediator, sent) = CapturingMediator();

        await BuildJob(db, mediator).CheckOverdueMaintenanceAsync();

        sent.Should().BeEmpty();
    }

    [Fact]
    public async Task ShotBasedSchedule_PastInterval_Notifies()
    {
        using var db = TestDbContextFactory.Create();
        await SeedScheduleAsync(db, currentShots: 50_500, intervalShots: 50_000);
        var (mediator, sent) = CapturingMediator();

        await BuildJob(db, mediator).CheckOverdueMaintenanceAsync();

        sent.Should().ContainSingle();
        sent[0].Message.Should().Contain("50,500 shots since last service (interval 50,000)");
    }

    [Fact]
    public async Task ShotBasedSchedule_BelowInterval_DoesNotNotify()
    {
        using var db = TestDbContextFactory.Create();
        await SeedScheduleAsync(db, currentShots: 49_999, intervalShots: 50_000);
        var (mediator, sent) = CapturingMediator();

        await BuildJob(db, mediator).CheckOverdueMaintenanceAsync();

        sent.Should().BeEmpty();
    }

    [Fact]
    public async Task UsageDueSchedule_AlreadyNotified_IsNotNotifiedAgain()
    {
        using var db = TestDbContextFactory.Create();
        var (asset, _) = await SeedScheduleAsync(db, currentHours: 260m, intervalHours: 250m);
        db.Notifications.Add(new Notification
        {
            UserId = 7, Type = "alert", Source = "maintenance-overdue",
            Title = "Overdue Maintenance: Spindle lube", EntityType = "Asset", EntityId = asset.Id,
        });
        await db.SaveChangesAsync();
        var (mediator, sent) = CapturingMediator();

        await BuildJob(db, mediator).CheckOverdueMaintenanceAsync();

        sent.Should().BeEmpty();
    }

    [Fact]
    public async Task TwoDateOverdueSchedulesOnOneAsset_BothNotify()
    {
        using var db = TestDbContextFactory.Create();
        var (asset, _) = await SeedScheduleAsync(db, nextDueAt: Now.AddDays(-2));
        db.MaintenanceSchedules.Add(new MaintenanceSchedule { AssetId = asset.Id, Title = "Way cover inspection", IntervalDays = 30, NextDueAt = Now.AddDays(-1) });
        await db.SaveChangesAsync();
        var (mediator, sent) = CapturingMediator();

        await BuildJob(db, mediator).CheckOverdueMaintenanceAsync();

        sent.Select(n => n.Title).Should().BeEquivalentTo(
            "Overdue Maintenance: Spindle lube", "Overdue Maintenance: Way cover inspection");
    }
}
