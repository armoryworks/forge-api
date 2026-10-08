using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Jobs.Operations;
using Forge.Api.Hubs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs.Operations;

[Collection(PostgresCollection.Name)]
public sealed class JobOperationTimerPostgresTests(PostgresFixture fixture)
{
    private const int UserId = 424242;

    private static StartJobOperationTimerHandler Handler(AppDbContext db)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetBoolAsync(ShopFloorSettings.OperationTrackingKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(() => DateTimeOffset.UtcNow);
        var http = new Mock<IHttpContextAccessor>();
        http.Setup(h => h.HttpContext).Returns(() => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "test")),
        });
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDetailResponseModel)null!);

        return new StartJobOperationTimerHandler(
            new JobOperationService(db, settings.Object, clock.Object),
            new TimeTrackingRepository(db),
            db,
            http.Object,
            HubWithGroups<TimerHub>(),
            HubWithGroups<BoardHub>(),
            mediator.Object,
            clock.Object);
    }

    private static IHubContext<T> HubWithGroups<T>() where T : Hub
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var hub = new Mock<IHubContext<T>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        return hub.Object;
    }

    private async Task<(int JobId, int OperationId)> SeedAsync()
    {
        await using var seed = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var track = new TrackType { Name = $"OpTrack {suffix}", Code = $"optrack-{suffix}", IsActive = true };
        seed.TrackTypes.Add(track);
        await seed.SaveChangesAsync();
        var stage = new JobStage { TrackTypeId = track.Id, Name = "Machining", Code = "machining", SortOrder = 1, IsActive = true };
        var part = new Part { PartNumber = $"OP-{suffix}", Name = "Bracket" };
        seed.AddRange(stage, part);
        await seed.SaveChangesAsync();
        var operation = new Operation { PartId = part.Id, StepNumber = 10, Title = "Mill", RunMinutesEach = 2m };
        var job = new Job
        {
            JobNumber = $"J-OP-{suffix}",
            Title = "Operation timer race",
            TrackTypeId = track.Id,
            CurrentStageId = stage.Id,
            PartId = part.Id,
        };
        seed.Operations.Add(operation);
        seed.Jobs.Add(job);
        await seed.SaveChangesAsync();
        return (job.Id, operation.Id);
    }

    [Fact]
    public async Task Two_simultaneous_starts_on_one_step_leave_one_open_timer_and_one_row()
    {
        var (jobId, operationId) = await SeedAsync();
        await using var firstDb = fixture.CreateContext();
        await using var secondDb = fixture.CreateContext();
        var command = new StartJobOperationTimerCommand(jobId, operationId, new StartJobOperationTimerRequestModel());

        var results = await Task.WhenAll(
            Handler(firstDb).Handle(command, CancellationToken.None),
            Handler(secondDb).Handle(command, CancellationToken.None));

        results.Select(r => r.Entry.Id).Distinct().Should().ContainSingle();
        results.Count(r => !r.AlreadyRunning).Should().BeLessThanOrEqualTo(1);

        await using var verify = fixture.CreateContext();
        (await verify.JobOperations.CountAsync(r => r.JobId == jobId)).Should().Be(1);
        (await verify.TimeEntries.CountAsync(t => t.JobId == jobId && t.TimerStop == null)).Should().Be(1);
    }

    [Fact]
    public async Task The_database_refuses_a_second_open_timer_for_the_same_user_and_step()
    {
        var (jobId, operationId) = await SeedAsync();
        await using var db = fixture.CreateContext();
        await Handler(db).Handle(
            new StartJobOperationTimerCommand(jobId, operationId, new StartJobOperationTimerRequestModel()),
            CancellationToken.None);
        var rowId = await db.JobOperations.Where(r => r.JobId == jobId).Select(r => r.Id).SingleAsync();

        await using var raw = fixture.CreateContext();
        raw.TimeEntries.Add(new TimeEntry
        {
            UserId = UserId,
            JobId = jobId,
            OperationId = operationId,
            JobOperationId = rowId,
            Date = DateOnly.FromDateTime(DateTime.UnixEpoch),
            TimerStart = DateTimeOffset.UnixEpoch,
        });
        var act = () => raw.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<Npgsql.PostgresException>()
            .Which.ConstraintName.Should().Be("ux_time_entries_open_job_operation");
    }
}
