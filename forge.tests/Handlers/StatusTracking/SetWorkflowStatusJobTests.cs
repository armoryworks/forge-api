using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Data;
using Forge.Api.Features.StatusTracking;
using Forge.Api.Hubs;
using Forge.Api.Middleware;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.StatusTracking;

public class SetWorkflowStatusJobTests
{
    private const int TrackTypeId = 7;
    private const string JobArchiveCapability = "CAP-MFG-WO-RELEASE";

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IStatusEntryRepository> _statusRepo = new();
    private readonly Mock<IClientProxy> _boardGroup = new();
    private readonly Mock<IHubClients> _hubClients = new();
    private readonly Mock<IHubContext<BoardHub>> _boardHub = new();
    private readonly Mock<ISystemAuditWriter> _auditWriter = new();
    private readonly HttpContextAccessor _httpContext = new();
    private readonly CapabilityStub _capabilities = new();
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

        SignInAs("Admin");

        _handler = new SetWorkflowStatusHandler(
            _db,
            _statusRepo.Object,
            new ActivityLogRepository(_db),
            Mock.Of<IWorkCenterContext>(),
            _httpContext,
            _boardHub.Object,
            _capabilities,
            _auditWriter.Object,
            new SystemClock());
    }

    [Fact]
    public async Task SettingArchived_ArchivesTheJobAndRemovesItFromTheBoard()
    {
        SignInAs("ProductionWorker");
        var job = await SeedJobAsync(isArchived: false);

        await _handler.Handle(Command(job.Id, SetWorkflowStatusHandler.JobArchivedStatusCode), CancellationToken.None);

        (await _db.Jobs.FindAsync(job.Id))!.IsArchived.Should().BeTrue();
        VerifyBoardUpdated("archive", Times.Once());
        _db.JobActivityLogs.Should().ContainSingle(l => l.JobId == job.Id && l.Action == ActivityAction.Archived);
    }

    [Fact]
    public async Task SettingArchived_WithoutAJobRole_IsForbiddenAndLeavesTheJobOnTheBoard()
    {
        SignInAs("Procurement");
        var job = await SeedJobAsync(isArchived: false);

        var act = () => _handler.Handle(
            Command(job.Id, SetWorkflowStatusHandler.JobArchivedStatusCode), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        (await _db.Jobs.FindAsync(job.Id))!.IsArchived.Should().BeFalse();
        _db.StatusEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task SettingArchived_WithJobReleaseDisabled_IsRejected()
    {
        _capabilities.Disable(JobArchiveCapability);
        var job = await SeedJobAsync(isArchived: false);

        var act = () => _handler.Handle(
            Command(job.Id, SetWorkflowStatusHandler.JobArchivedStatusCode), CancellationToken.None);

        await act.Should().ThrowAsync<CapabilityDisabledException>();
        (await _db.Jobs.FindAsync(job.Id))!.IsArchived.Should().BeFalse();
    }

    [Fact]
    public async Task AdminLeavingArchived_UnarchivesTheJobAndRestoresItToTheBoard()
    {
        var job = await SeedJobAsync(isArchived: false);
        await _handler.Handle(Command(job.Id, SetWorkflowStatusHandler.JobArchivedStatusCode), CancellationToken.None);

        await _handler.Handle(Command(job.Id, "job_status_in_progress"), CancellationToken.None);

        (await _db.Jobs.FindAsync(job.Id))!.IsArchived.Should().BeFalse();
        VerifyBoardUpdated("archive", Times.Once());
        VerifyBoardUpdated("unarchive", Times.Once());
        _db.JobActivityLogs.Should().ContainSingle(l => l.JobId == job.Id && l.Action == ActivityAction.Restored);
        _auditWriter.Verify(a => a.WriteAsync(
            "JobUnarchived", It.IsAny<int>(), "Job", job.Id, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task NonAdminLeavingArchived_IsForbiddenAndLeavesTheJobArchived()
    {
        SignInAs("Manager");
        var job = await SeedJobAsync(isArchived: false);
        await _handler.Handle(Command(job.Id, SetWorkflowStatusHandler.JobArchivedStatusCode), CancellationToken.None);

        var act = () => _handler.Handle(Command(job.Id, "job_status_in_progress"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        (await _db.Jobs.FindAsync(job.Id))!.IsArchived.Should().BeTrue();
        _auditWriter.Verify(a => a.WriteAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Controller")]
    [InlineData("Admin")]
    public async Task ArchivedThenAnotherStatus_OnABulkArchivedJob_LeavesItArchived(string role)
    {
        SignInAs(role);
        var job = await SeedBulkArchivedJobAsync();

        await _handler.Handle(Command(job.Id, SetWorkflowStatusHandler.JobArchivedStatusCode), CancellationToken.None);
        await _handler.Handle(Command(job.Id, "job_status_in_progress"), CancellationToken.None);

        (await _db.Jobs.FindAsync(job.Id))!.IsArchived.Should().BeTrue();
        _db.JobActivityLogs.Should().NotContain(l => l.JobId == job.Id && l.Action == ActivityAction.Restored);
        _boardGroup.Verify(p => p.SendCoreAsync(
            It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OtherStatus_OnAJobArchivedElsewhere_LeavesItArchived()
    {
        var job = await SeedBulkArchivedJobAsync();

        await _handler.Handle(Command(job.Id, "job_status_on_hold"), CancellationToken.None);

        (await _db.Jobs.FindAsync(job.Id))!.IsArchived.Should().BeTrue();
        _boardGroup.Verify(p => p.SendCoreAsync(
            It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ArchivedStatus_OnANonJobEntity_TouchesNoJob()
    {
        SignInAs("Controller");
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

    private async Task<Job> SeedBulkArchivedJobAsync()
    {
        var job = await SeedJobAsync(isArchived: true);
        _db.JobActivityLogs.Add(new JobActivityLog
        {
            JobId = job.Id,
            Action = ActivityAction.Archived,
            Description = "Archived (bulk).",
        });
        await _db.SaveChangesAsync();
        return job;
    }

    private void SignInAs(string role) =>
        _httpContext.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, role)], "Test")),
        };

    private void VerifyBoardUpdated(string reason, Times times) =>
        _boardGroup.Verify(p => p.SendCoreAsync(
            "boardUpdated",
            It.Is<object?[]>(args => args.Length == 1 && ReasonOf(args[0]) == reason),
            It.IsAny<CancellationToken>()), times);

    private static string? ReasonOf(object? payload) =>
        payload?.GetType().GetProperty("reason")?.GetValue(payload) as string;

    private static SetWorkflowStatusCommand Command(int jobId, string statusCode) =>
        new("job", jobId, new SetStatusRequestModel(statusCode, null));

    private sealed class CapabilityStub : ICapabilitySnapshotProvider
    {
        private readonly HashSet<string> _disabled = new(StringComparer.Ordinal);

        public CapabilitySnapshot Current => new(new Dictionary<string, bool>(), DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => !_disabled.Contains(code);

        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;

        public void Disable(string code) => _disabled.Add(code);
    }
}
