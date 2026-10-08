using MediatR;
using Microsoft.AspNetCore.SignalR;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Jobs.Operations;
using Forge.Api.Hubs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Core.Settings;

namespace Forge.Tests.Helpers;

/// <summary>
/// <see cref="TimerTestHarness"/> plus what the operation-tracking handlers need: the
/// operation-tracking setting (on unless a test turns it off), a board hub, a real
/// <see cref="JobOperationService"/>, and a seeded part whose routing has steps 10, 20 and 30.
/// </summary>
public sealed class JobOperationTestHarness
{
    public TimerTestHarness Timers { get; } = new();
    public Mock<ISettingsService> Settings { get; } = new();
    public Mock<IHubContext<BoardHub>> BoardHub { get; } = new();
    public Mock<IClientProxy> BoardGroup { get; } = new();
    public bool TrackingEnabled { get; set; } = true;
    public JobOperationService Operations { get; }

    public JobOperationTestHarness()
    {
        Settings.Setup(s => s.GetBoolAsync(ShopFloorSettings.OperationTrackingKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => TrackingEnabled);

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(BoardGroup.Object);
        BoardHub.Setup(h => h.Clients).Returns(clients.Object);

        Timers.Mediator
            .Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDetailResponseModel)null!);

        Operations = new JobOperationService(Timers.Db, Settings.Object, Timers.Clock.Object);
    }

    public StartJobOperationTimerHandler StartHandler(int userId) => new(
        Operations, Timers.Repo, Timers.Db, Timers.SignedIn(userId), Timers.TimerHub.Object,
        BoardHub.Object, Timers.Mediator.Object, Timers.Clock.Object);

    public StopJobOperationTimerHandler StopHandler(int userId) => new(
        Timers.Repo, Timers.Db, Timers.SignedIn(userId), Timers.Mediator.Object, Timers.Clock.Object);

    public UpdateJobOperationProgressHandler ProgressHandler(int userId) => new(
        Operations, Timers.TimerStopService(), Timers.Db, Timers.SignedIn(userId), BoardHub.Object,
        Timers.Mediator.Object, Timers.Clock.Object);

    public async Task<(Job Job, List<Operation> Routing)> AddJobWithRoutingAsync(decimal quantity, string jobNumber = "JOB-0500")
    {
        var workCenter = new WorkCenter { Name = "Mill 1", Code = "M1" };
        var part = new Part { PartNumber = $"P-{jobNumber}", Name = "Bracket" };
        Timers.Db.AddRange(workCenter, part);
        await Timers.Db.SaveChangesAsync();

        var routing = new List<Operation>
        {
            new() { PartId = part.Id, StepNumber = 10, Title = "Saw", SetupMinutes = 15m, RunMinutesLot = 5m, RunMinutesEach = 3m, WorkCenterId = workCenter.Id },
            new() { PartId = part.Id, StepNumber = 20, Title = "Mill", SetupMinutes = 30m, EstimatedMs = 120000 },
            new() { PartId = part.Id, StepNumber = 30, Title = "Deburr", RunMinutesEach = 1m },
        };
        Timers.Db.Operations.AddRange(routing);

        var job = new Job
        {
            JobNumber = jobNumber,
            Title = jobNumber,
            TrackTypeId = 1,
            CurrentStageId = 1,
            PartId = part.Id,
            JobParts = [new JobPart { PartId = part.Id, Quantity = quantity }],
        };
        Timers.Db.Jobs.Add(job);
        await Timers.Db.SaveChangesAsync();
        return (job, routing);
    }
}
