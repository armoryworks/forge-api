using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

using Forge.Api.Capabilities;
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

namespace Forge.Tests.Handlers.Jobs.Operations;

[Collection(PostgresCollection.Name)]
public sealed class StepGatingPostgresTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 16, 0, 0, TimeSpan.Zero);
    private int _userId;
    private string[] _roles = [];

    private IHttpContextAccessor Caller()
    {
        var http = new Mock<IHttpContextAccessor>();
        http.Setup(h => h.HttpContext).Returns(() => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, _userId.ToString()), .. _roles.Select(r => new Claim(ClaimTypes.Role, r))], "test")),
        });
        return http.Object;
    }

    private (StopJobOperationTimerHandler Stop, UpdateJobOperationProgressHandler Progress) Build(AppDbContext db)
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetBoolAsync(ShopFloorSettings.OperationTrackingKey, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        settings.Setup(s => s.GetStringAsync(ShopFloorSettings.StepGatingKey, It.IsAny<CancellationToken>())).ReturnsAsync("Off");
        settings.Setup(s => s.GetStringAsync(ShopFloorSettings.LaborSplitKey, It.IsAny<CancellationToken>())).ReturnsAsync("Weighted");
        var capabilities = new Mock<ICapabilitySnapshotProvider>();
        capabilities.Setup(c => c.IsEnabled(It.IsAny<string>())).Returns(true);
        var providers = new Mock<IAccountingProviderFactory>();
        providers.Setup(p => p.GetActiveProviderAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IAccountingService?)null);
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        var eventTypes = new Mock<IClockEventTypeService>();
        eventTypes.Setup(t => t.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        clients.Setup(c => c.All).Returns(Mock.Of<IClientProxy>());
        var timerHub = new Mock<IHubContext<TimerHub>>();
        timerHub.Setup(h => h.Clients).Returns(clients.Object);
        var boardHub = new Mock<IHubContext<BoardHub>>();
        boardHub.Setup(h => h.Clients).Returns(clients.Object);

        var jobCost = new JobCostService(db);
        var allocation = new LaborAllocationService(db, settings.Object, eventTypes.Object, clock.Object, jobCost);
        var stops = new TimerStopService(
            new TimeTrackingRepository(db), db, timerHub.Object, new SyncQueueRepository(db), providers.Object,
            userManager.Object, new JobRepository(db, clock.Object), Mock.Of<ICustomerRepository>(),
            Mock.Of<ILogger<TimerStopService>>(), jobCost, allocation);
        var mediator = new Mock<IMediator>();
        var progress = new UpdateJobOperationProgressHandler(
            new JobOperationService(db, settings.Object, clock.Object), stops, db, Caller(), boardHub.Object,
            mediator.Object, clock.Object, null, new StepGateService(db, settings.Object, capabilities.Object));
        mediator.Setup(m => m.Send(It.IsAny<StopActiveTimerCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<StoppedTimerResponseModel?> command, CancellationToken ct) =>
                new StopActiveTimerHandler(new TimeTrackingRepository(db), stops).Handle((StopActiveTimerCommand)command, ct));
        mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDetailResponseModel)null!);
        mediator.Setup(m => m.Send(It.IsAny<UpdateJobOperationProgressCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<JobOperationProgressResponseModel> command, CancellationToken ct) =>
                progress.Handle((UpdateJobOperationProgressCommand)command, ct));
        var stop = new StopJobOperationTimerHandler(new TimeTrackingRepository(db), db, Caller(), mediator.Object, clock.Object);
        return (stop, progress);
    }

    private async Task<(int JobId, int Saw, int Mill)> SeedAsync(StepGatingMode partGating)
    {
        await using var seed = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new ApplicationUser
        {
            FirstName = "Robin", LastName = $"Gate{suffix}", UserName = $"gate-{suffix}@example.com",
            Email = $"gate-{suffix}@example.com", IsActive = true,
        };
        var track = new TrackType { Name = $"Gate {suffix}", Code = $"gate-{suffix}", IsActive = true };
        var part = new Part { PartNumber = $"G-{suffix}", Name = "Bracket", StepGating = partGating };
        seed.AddRange(user, track, part);
        await seed.SaveChangesAsync();
        _userId = user.Id;
        var stage = new JobStage { TrackTypeId = track.Id, Name = "Machining", Code = "machining", SortOrder = 1, IsActive = true };
        var saw = new Operation { PartId = part.Id, StepNumber = 10, Title = "Saw" };
        var mill = new Operation { PartId = part.Id, StepNumber = 20, Title = "Mill" };
        seed.AddRange(stage, saw, mill);
        await seed.SaveChangesAsync();
        var job = new Job { JobNumber = $"J-G-{suffix}", Title = "Gate", TrackTypeId = track.Id, CurrentStageId = stage.Id, PartId = part.Id };
        seed.Jobs.Add(job);
        await seed.SaveChangesAsync();
        seed.JobParts.Add(new JobPart { JobId = job.Id, PartId = part.Id, Quantity = 10m });
        seed.JobOperations.Add(new JobOperation { JobId = job.Id, OperationId = saw.Id, StepNumber = 10, Title = "Saw", Status = JobOperationStatus.InProgress, CompletedQuantity = 4m });
        await seed.SaveChangesAsync();
        return (job.Id, saw.Id, mill.Id);
    }

    [Fact]
    public async Task The_schema_refuses_a_self_link_and_an_unknown_gate()
    {
        var (_, saw, mill) = await SeedAsync(StepGatingMode.Off);

        await using var db = fixture.CreateContext();
        var self = async () => await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO operation_predecessors (operation_id, predecessor_operation_id, gate, created_at, updated_at) VALUES ({mill}, {mill}, 0, now(), now())");
        var gate = async () => await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO operation_predecessors (operation_id, predecessor_operation_id, gate, created_at, updated_at) VALUES ({mill}, {saw}, 7, now(), now())");

        await self.Should().ThrowAsync<Npgsql.PostgresException>().Where(e => e.ConstraintName == "ck_operation_predecessors_not_self");
        await gate.Should().ThrowAsync<Npgsql.PostgresException>().Where(e => e.ConstraintName == "ck_operation_predecessors_gate");
    }

    [Fact]
    public async Task Links_round_trip_and_cascade_with_their_operations()
    {
        var (_, saw, mill) = await SeedAsync(StepGatingMode.Off);
        await using (var db = fixture.CreateContext())
        {
            db.OperationPredecessors.Add(new OperationPredecessor { OperationId = mill, PredecessorOperationId = saw, Gate = PredecessorGate.Complete });
            await db.SaveChangesAsync();
        }

        await using (var verify = fixture.CreateContext())
        {
            var link = await verify.OperationPredecessors.AsNoTracking().SingleAsync(l => l.OperationId == mill);
            link.Gate.Should().Be(PredecessorGate.Complete);
            link.CreatedAt.Should().NotBe(default);
            await verify.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM operations WHERE id = {saw}");
            (await verify.OperationPredecessors.AnyAsync(l => l.OperationId == mill)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task A_blocked_stop_with_pieces_rolls_back_and_leaves_the_timer_running()
    {
        var (jobId, _, mill) = await SeedAsync(StepGatingMode.Block);
        await using (var db = fixture.CreateContext())
        {
            db.TimeEntries.Add(new TimeEntry
            {
                UserId = _userId, JobId = jobId, OperationId = mill, EntryType = TimeEntryType.Run,
                Date = DateOnly.FromDateTime(Now.UtcDateTime), TimerStart = Now.AddMinutes(-20),
            });
            await db.SaveChangesAsync();
            var row = new JobOperation { JobId = jobId, OperationId = mill, StepNumber = 20, Title = "Mill", Status = JobOperationStatus.InProgress };
            db.JobOperations.Add(row);
            await db.SaveChangesAsync();
            await db.TimeEntries.Where(t => t.JobId == jobId).ExecuteUpdateAsync(s => s.SetProperty(t => t.JobOperationId, row.Id));
        }

        await using (var db = fixture.CreateContext())
        {
            var act = () => Build(db).Stop.Handle(
                new StopJobOperationTimerCommand(jobId, mill, new StopJobOperationTimerRequestModel(GoodQuantity: 6m)), default);
            (await act.Should().ThrowAsync<StepGateException>()).Which.Violation.Available.Should().Be(4m);
        }

        await using var verify = fixture.CreateContext();
        (await verify.TimeEntries.AsNoTracking().SingleAsync(t => t.JobId == jobId)).TimerStop.Should().BeNull();
        (await verify.JobOperations.AsNoTracking().SingleAsync(r => r.OperationId == mill)).CompletedQuantity.Should().Be(0m);
    }

    [Fact]
    public async Task A_supervisor_override_is_stored_on_the_event()
    {
        var (jobId, _, mill) = await SeedAsync(StepGatingMode.Block);
        _roles = ["Production Manager"];

        await using (var db = fixture.CreateContext())
        {
            await Build(db).Progress.Handle(new UpdateJobOperationProgressCommand(jobId, mill,
                new UpdateJobOperationProgressRequestModel(6m, null, null, AcknowledgeGate: true, OverrideReason: "parts from stock")), default);
        }

        await using var verify = fixture.CreateContext();
        var evt = await verify.JobOperationEvents.AsNoTracking().SingleAsync(e => e.JobOperation.OperationId == mill);
        evt.GateOverride.Should().BeTrue();
        evt.GateOverrideReason.Should().Be("parts from stock");
        (await verify.Parts.AsNoTracking().SingleAsync(p => p.Operations.Any(o => o.Id == mill))).StepGating.Should().Be(StepGatingMode.Block);
    }
}
