using System.Security.Claims;

using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Jobs.Operations;
using Forge.Api.Features.TimeTracking;
using Forge.Api.Hubs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Costing;

[Collection(PostgresCollection.Name)]
public sealed class LaborSplitPostgresTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 3, 11, 18, 0, 0, TimeSpan.Zero);
    private const decimal RatePerHour = 60m;

    private int _userId;

    private static readonly List<ClockEventTypeDefinition> Definitions =
    [
        new("ClockIn", "Clock In", "In", "ClockOut", "work", true, true, "login", "#22c55e"),
        new("ClockOut", "Clock Out", "Out", "ClockIn", "work", false, false, "logout", "#ef4444"),
        new("BreakStart", "Start Break", "OnBreak", "BreakEnd", "break", true, true, "free_breakfast", "#f59e0b"),
        new("BreakEnd", "End Break", "In", "BreakStart", "break", true, false, "play_arrow", "#22c55e"),
    ];

    private sealed record Rig(
        AppDbContext Db, Mock<IMediator> Mediator, TimerStopService Stops, LaborAllocationService Allocation, IClock Clock);

    private Rig Build(AppDbContext db)
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetStringAsync(ShopFloorSettings.LaborSplitKey, It.IsAny<CancellationToken>())).ReturnsAsync("Weighted");
        settings.Setup(s => s.GetBoolAsync(ShopFloorSettings.BreaksPauseLaborKey, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        settings.Setup(s => s.GetBoolAsync(ShopFloorSettings.OperationTrackingKey, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var eventTypes = new Mock<IClockEventTypeService>();
        eventTypes.Setup(t => t.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Definitions);
        var providers = new Mock<IAccountingProviderFactory>();
        providers.Setup(p => p.GetActiveProviderAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IAccountingService?)null);
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var timerHub = new Mock<IHubContext<TimerHub>>();
        timerHub.Setup(h => h.Clients).Returns(clients.Object);

        var jobCost = new JobCostService(db);
        var allocation = new LaborAllocationService(db, settings.Object, eventTypes.Object, clock.Object, jobCost);
        var stops = new TimerStopService(
            new TimeTrackingRepository(db), db, timerHub.Object, new SyncQueueRepository(db), providers.Object,
            userManager.Object, new JobRepository(db, clock.Object), Mock.Of<ICustomerRepository>(),
            Mock.Of<ILogger<TimerStopService>>(), jobCost, allocation);

        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<StopActiveTimerCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<StoppedTimerResponseModel?> command, CancellationToken ct) =>
                new StopActiveTimerHandler(new TimeTrackingRepository(db), stops).Handle((StopActiveTimerCommand)command, ct));
        mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDetailResponseModel)null!);
        var boardClients = new Mock<IHubClients>();
        boardClients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        boardClients.Setup(c => c.All).Returns(Mock.Of<IClientProxy>());
        var boardHub = new Mock<IHubContext<BoardHub>>();
        boardHub.Setup(h => h.Clients).Returns(boardClients.Object);
        mediator.Setup(m => m.Send(It.IsAny<UpdateJobOperationProgressCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<JobOperationProgressResponseModel> command, CancellationToken ct) =>
                new UpdateJobOperationProgressHandler(
                    new JobOperationService(db, settings.Object, clock.Object), stops, db, Caller(), boardHub.Object,
                    mediator.Object, clock.Object).Handle((UpdateJobOperationProgressCommand)command, ct));

        return new Rig(db, mediator, stops, allocation, clock.Object);
    }

    private IHttpContextAccessor Caller()
    {
        var http = new Mock<IHttpContextAccessor>();
        http.Setup(h => h.HttpContext).Returns(() => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, _userId.ToString())], "test")),
        });
        return http.Object;
    }

    private async Task<(int JobA, int JobB, int OpTurn, int OpDeburr, int OpInspect)> SeedAsync()
    {
        await using var seed = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new ApplicationUser
        {
            FirstName = "Avery", LastName = $"Split{suffix}", UserName = $"split-{suffix}@example.com",
            Email = $"split-{suffix}@example.com", IsActive = true,
        };
        seed.Users.Add(user);
        var track = new TrackType { Name = $"Split {suffix}", Code = $"split-{suffix}", IsActive = true };
        seed.TrackTypes.Add(track);
        await seed.SaveChangesAsync();
        _userId = user.Id;
        var stage = new JobStage { TrackTypeId = track.Id, Name = "Machining", Code = "machining", SortOrder = 1, IsActive = true };
        var lathe = new WorkCenter { Name = $"Lathe {suffix}", Code = $"L-{suffix}", BurdenRatePerHour = 30m, BurdenBasis = BurdenBasis.Machine, IsActive = true };
        var bench = new WorkCenter { Name = $"Bench {suffix}", Code = $"B-{suffix}", BurdenRatePerHour = 30m, IsActive = true };
        var partA = new Part { PartNumber = $"SA-{suffix}", Name = "Shaft" };
        var partB = new Part { PartNumber = $"SB-{suffix}", Name = "Bushing" };
        seed.AddRange(stage, lathe, bench, partA, partB);
        await seed.SaveChangesAsync();
        var turn = new Operation { PartId = partA.Id, StepNumber = 20, Title = "Turn", WorkCenterId = lathe.Id, LaborAttendancePercent = 20m };
        var deburr = new Operation { PartId = partB.Id, StepNumber = 80, Title = "Deburr", WorkCenterId = bench.Id };
        var inspect = new Operation { PartId = partB.Id, StepNumber = 40, Title = "Inspect", WorkCenterId = bench.Id };
        var jobA = new Job { JobNumber = $"J-SA-{suffix}", Title = "A", TrackTypeId = track.Id, CurrentStageId = stage.Id, PartId = partA.Id };
        var jobB = new Job { JobNumber = $"J-SB-{suffix}", Title = "B", TrackTypeId = track.Id, CurrentStageId = stage.Id, PartId = partB.Id };
        seed.AddRange(turn, deburr, inspect, jobA, jobB);
        seed.LaborRates.Add(new LaborRate { UserId = _userId, StandardRatePerHour = RatePerHour, EffectiveFrom = new DateOnly(2026, 1, 1) });
        await seed.SaveChangesAsync();
        seed.JobParts.AddRange(
            new JobPart { JobId = jobA.Id, PartId = partA.Id, Quantity = 10m },
            new JobPart { JobId = jobB.Id, PartId = partB.Id, Quantity = 10m });
        await seed.SaveChangesAsync();
        return (jobA.Id, jobB.Id, turn.Id, deburr.Id, inspect.Id);
    }

    private async Task<int> AddTimerAsync(int? jobId, int? operationId, DateTimeOffset start)
    {
        await using var db = fixture.CreateContext();
        var entry = new TimeEntry
        {
            UserId = _userId,
            JobId = jobId,
            OperationId = operationId,
            EntryType = TimeEntryType.Run,
            Date = DateOnly.FromDateTime(start.UtcDateTime),
            TimerStart = start,
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task AddClockAsync(string code, DateTimeOffset at)
    {
        await using var db = fixture.CreateContext();
        db.ClockEvents.Add(new ClockEvent { UserId = _userId, EventTypeCode = code, Timestamp = at, Source = "kiosk" });
        await db.SaveChangesAsync();
    }

    private async Task ClockOutAsync()
    {
        await using var db = fixture.CreateContext();
        var rig = Build(db);
        await new StopActiveTimerHandler(new TimeTrackingRepository(db), rig.Stops)
            .Handle(new StopActiveTimerCommand(_userId, Now, Reason: "clocked out", IncludeOperationTimers: true), CancellationToken.None);
    }

    [Fact]
    public async Task Clock_out_with_three_step_timers_on_two_jobs_charges_exactly_the_clocked_minutes_once()
    {
        var (jobA, jobB, turn, deburr, inspect) = await SeedAsync();
        await AddClockAsync("ClockIn", Now.AddMinutes(-50));
        await AddTimerAsync(jobA, turn, Now.AddMinutes(-35));
        await AddTimerAsync(jobB, deburr, Now.AddMinutes(-35));
        await AddTimerAsync(jobB, inspect, Now.AddMinutes(-35));

        await ClockOutAsync();

        await using var verify = fixture.CreateContext();
        var entries = await verify.TimeEntries.AsNoTracking().Where(t => t.UserId == _userId).OrderBy(t => t.Id).ToListAsync();
        entries.Select(e => e.LaborMinutes).Should().Equal(3.18m, 15.91m, 15.91m);
        entries.Sum(e => e.LaborCost).Should().Be(35m, "labor cost / rate equals the 35 clocked minutes");
        var jobCost = new JobCostService(verify);
        (await jobCost.GetActualLaborCostAsync(jobA, default) + await jobCost.GetActualLaborCostAsync(jobB, default))
            .Should().Be(35m, "absorption reads one times the labor, not three");
        (await jobCost.GetActualBurdenCostAsync(jobA, default)).Should().Be(17.5m, "the Machine-basis lathe burdens the full 35-minute cycle");
        (await jobCost.GetActualBurdenCostAsync(jobB, default)).Should().Be(15.91m, "the Labor-basis bench burdens the 31.82 labor minutes");
    }

    [Fact]
    public async Task Editing_one_stop_time_reallocates_its_siblings()
    {
        var (jobA, jobB, _, deburr, inspect) = await SeedAsync();
        var shortId = await AddTimerAsync(jobB, deburr, Now.AddMinutes(-60));
        var longId = await AddTimerAsync(jobB, inspect, Now.AddMinutes(-60));
        await using (var db = fixture.CreateContext())
        {
            var rig = Build(db);
            var shortEntry = await db.TimeEntries.SingleAsync(t => t.Id == shortId);
            rig.Stops.Close(shortEntry, Now.AddMinutes(-30));
            await db.SaveChangesAsync();
            await rig.Stops.PublishStoppedAsync([shortEntry], default);
        }
        await ClockOutAsync();

        await using (var db = fixture.CreateContext())
        {
            var rig = Build(db);
            await new AdminCorrectTimeEntryHandler(db, new TimeTrackingRepository(db), rig.Mediator.Object, new JobCostService(db), rig.Allocation)
                .Handle(new AdminCorrectTimeEntryCommand(shortId, _userId,
                    new AdminCorrectTimeEntryRequestModel(null, null, null, Now.AddMinutes(-60), Now.AddMinutes(-50), null, null, "wrong stop")),
                    CancellationToken.None);
        }

        await using var verify = fixture.CreateContext();
        (await verify.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == shortId)).LaborMinutes.Should().Be(5m);
        var sibling = await verify.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == longId);
        sibling.LaborMinutes.Should().Be(55m);
        sibling.LaborCost.Should().Be(55m);
    }

    [Fact]
    public async Task An_edit_reaching_a_locked_entry_is_refused_and_changes_nothing()
    {
        var (_, jobB, _, deburr, inspect) = await SeedAsync();
        var shortId = await AddTimerAsync(jobB, deburr, Now.AddMinutes(-60));
        var longId = await AddTimerAsync(jobB, inspect, Now.AddMinutes(-60));
        await ClockOutAsync();
        await using (var db = fixture.CreateContext())
        {
            await db.TimeEntries.Where(t => t.Id == longId).ExecuteUpdateAsync(u => u.SetProperty(t => t.IsLocked, true));
        }

        await using (var db = fixture.CreateContext())
        {
            var rig = Build(db);
            var act = () => new AdminCorrectTimeEntryHandler(db, new TimeTrackingRepository(db), rig.Mediator.Object, new JobCostService(db), rig.Allocation)
                .Handle(new AdminCorrectTimeEntryCommand(shortId, _userId,
                    new AdminCorrectTimeEntryRequestModel(null, null, null, Now.AddMinutes(-60), Now.AddMinutes(-50), null, null, "wrong stop")),
                    CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*locked pay period*");
        }

        await using var verify = fixture.CreateContext();
        (await verify.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == longId)).LaborMinutes.Should().Be(30m);
        (await verify.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == shortId)).TimerStop.Should().Be(Now);
    }

    [Fact]
    public async Task Stop_with_quantities_is_atomic()
    {
        var (_, jobB, _, deburr, _) = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            db.JobOperations.Add(new JobOperation { JobId = jobB, OperationId = deburr, StepNumber = 80, Title = "Deburr", Status = JobOperationStatus.InProgress });
            await db.SaveChangesAsync();
            var rowId = await db.JobOperations.Where(r => r.JobId == jobB && r.OperationId == deburr).Select(r => r.Id).SingleAsync();
            db.TimeEntries.Add(new TimeEntry
            {
                UserId = _userId, JobId = jobB, OperationId = deburr, JobOperationId = rowId, EntryType = TimeEntryType.Run,
                Date = DateOnly.FromDateTime(Now.UtcDateTime), TimerStart = Now.AddMinutes(-20),
            });
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            var rig = Build(db);
            var tooMany = () => new StopJobOperationTimerHandler(new TimeTrackingRepository(db), db, Caller(), rig.Mediator.Object, rig.Clock)
                .Handle(new StopJobOperationTimerCommand(jobB, deburr, new StopJobOperationTimerRequestModel(GoodQuantity: 8m, ScrapQuantity: 5m)), CancellationToken.None);
            await tooMany.Should().ThrowAsync<ValidationException>();
        }

        await using (var verify = fixture.CreateContext())
        {
            (await verify.TimeEntries.CountAsync(t => t.UserId == _userId && t.TimerStop == null)).Should().Be(1);
            (await verify.JobOperations.SingleAsync(r => r.JobId == jobB && r.OperationId == deburr)).CompletedQuantity.Should().Be(0m);
        }

        await using (var db = fixture.CreateContext())
        {
            var rig = Build(db);
            var result = await new StopJobOperationTimerHandler(new TimeTrackingRepository(db), db, Caller(), rig.Mediator.Object, rig.Clock)
                .Handle(new StopJobOperationTimerCommand(jobB, deburr, new StopJobOperationTimerRequestModel(GoodQuantity: 6m, ScrapQuantity: 1m)), CancellationToken.None);
            result.Stopped.Should().BeTrue();
        }

        await using var after = fixture.CreateContext();
        (await after.TimeEntries.CountAsync(t => t.UserId == _userId && t.TimerStop == null)).Should().Be(0);
        var row = await after.JobOperations.SingleAsync(r => r.JobId == jobB && r.OperationId == deburr);
        row.CompletedQuantity.Should().Be(6m);
        row.ScrapQuantity.Should().Be(1m);
    }
}
