using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;

using Forge.Api.Data;
using Forge.Api.Features.StatusTracking;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.StatusTracking;

public class SetWorkflowStatusJobTests
{
    private const int TrackTypeId = 7;

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IStatusEntryRepository> _statusRepo = new();
    private readonly Mock<IActivityLogRepository> _activityRepo = new();
    private readonly Mock<IClientProxy> _boardGroup = new();
    private readonly Mock<IHubClients> _hubClients = new();
    private readonly Mock<IHubContext<BoardHub>> _boardHub = new();
    private readonly SetWorkflowStatusHandler _handler;

    public SetWorkflowStatusJobTests()
    {
        _hubClients.Setup(c => c.Group($"board:{TrackTypeId}")).Returns(_boardGroup.Object);
        _boardHub.Setup(h => h.Clients).Returns(_hubClients.Object);

        _statusRepo.Setup(r => r.GetHistoryAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _db.StatusEntries
                .Select(e => new StatusEntryResponseModel(e.Id, e.EntityType, e.EntityId, e.StatusCode, e.StatusLabel,
                    e.Category, e.StartedAt, e.EndedAt, e.Notes, null, null, e.CreatedAt))
                .ToList());

        _handler = new SetWorkflowStatusHandler(
            _db,
            _statusRepo.Object,
            _activityRepo.Object,
            Mock.Of<IWorkCenterContext>(),
            Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            _boardHub.Object,
            new SystemClock());
    }

    [Fact]
    public async Task SettingArchived_ArchivesTheJobAndRemovesItFromTheBoard()
    {
        var job = await SeedJobAsync(isArchived: false);

        await _handler.Handle(Command(job.Id, SetWorkflowStatusHandler.JobArchivedStatusCode), CancellationToken.None);

        (await _db.Jobs.FindAsync(job.Id))!.IsArchived.Should().BeTrue();
        _boardGroup.Verify(p => p.SendCoreAsync(
            "boardUpdated",
            It.Is<object?[]>(args => args.Length == 1),
            It.IsAny<CancellationToken>()), Times.Once);
        _activityRepo.Verify(r => r.AddAsync(
            It.Is<JobActivityLog>(l => l.JobId == job.Id && l.Action == ActivityAction.Archived),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeavingArchived_UnarchivesTheJobAndRestoresItToTheBoard()
    {
        var job = await SeedJobAsync(isArchived: false);
        await _handler.Handle(Command(job.Id, SetWorkflowStatusHandler.JobArchivedStatusCode), CancellationToken.None);

        await _handler.Handle(Command(job.Id, "job_status_in_progress"), CancellationToken.None);

        (await _db.Jobs.FindAsync(job.Id))!.IsArchived.Should().BeFalse();
        _boardGroup.Verify(p => p.SendCoreAsync(
            "boardUpdated", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _activityRepo.Verify(r => r.AddAsync(
            It.Is<JobActivityLog>(l => l.JobId == job.Id && l.Action == ActivityAction.Restored),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OtherStatus_OnAJobArchivedElsewhere_LeavesItArchived()
    {
        var job = await SeedJobAsync(isArchived: true);

        await _handler.Handle(Command(job.Id, "job_status_on_hold"), CancellationToken.None);

        (await _db.Jobs.FindAsync(job.Id))!.IsArchived.Should().BeTrue();
        _boardGroup.Verify(p => p.SendCoreAsync(
            It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ArchivedStatus_OnANonJobEntity_TouchesNoJob()
    {
        var job = await SeedJobAsync(isArchived: false);

        await _handler.Handle(
            new SetWorkflowStatusCommand("asset", job.Id,
                new SetStatusRequestModel(SetWorkflowStatusHandler.JobArchivedStatusCode, null)),
            CancellationToken.None);

        (await _db.Jobs.FindAsync(job.Id))!.IsArchived.Should().BeFalse();
        _boardGroup.Verify(p => p.SendCoreAsync(
            It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void FreshSeed_DoesNotOfferCompletedAmongJobWorkflowStatuses()
    {
        var offered = SeedData.JobWorkflowStatuses().Where(r => r.IsActive).Select(r => r.Code).ToList();

        offered.Should().NotContain(SeedData.JobCompletedWorkflowStatusCode);
        offered.Should().Contain(SetWorkflowStatusHandler.JobArchivedStatusCode);
    }

    [Fact]
    public async Task ExistingInstall_RetiresCompletedOnce()
    {
        _db.ReferenceData.Add(new ReferenceData
        {
            IsSeedData = true,
            GroupCode = "job_workflow_status",
            Code = SeedData.JobCompletedWorkflowStatusCode,
            Label = "Completed",
            SortOrder = 4,
            IsActive = true,
        });
        await _db.SaveChangesAsync();

        await SeedData.RetireJobCompletedWorkflowStatusAsync(_db);

        var completed = _db.ReferenceData.Single(r => r.Code == SeedData.JobCompletedWorkflowStatusCode);
        completed.IsActive.Should().BeFalse();

        completed.IsActive = true;
        await _db.SaveChangesAsync();
        await SeedData.RetireJobCompletedWorkflowStatusAsync(_db);

        _db.ReferenceData.Single(r => r.Code == SeedData.JobCompletedWorkflowStatusCode).IsActive.Should().BeTrue();
    }

    private async Task<Job> SeedJobAsync(bool isArchived)
    {
        var job = new Job
        {
            JobNumber = $"J-{Guid.NewGuid():N}"[..12],
            Title = "Workflow archive job",
            TrackTypeId = TrackTypeId,
            CurrentStageId = 1,
            IsArchived = isArchived,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    private static SetWorkflowStatusCommand Command(int jobId, string statusCode) =>
        new("job", jobId, new SetStatusRequestModel(statusCode, null));
}
