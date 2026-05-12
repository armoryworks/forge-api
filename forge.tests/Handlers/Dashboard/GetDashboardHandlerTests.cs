using FluentAssertions;
using Moq;

using Forge.Api.Features.Dashboard;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Dashboard;

public class GetDashboardHandlerTests
{
    private readonly Mock<IDashboardRepository> _repo = new();

    [Fact]
    public async Task Handle_NoProductionTrack_ReturnsEmptyDashboard()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        _repo.Setup(r => r.GetDashboardDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardDataSet(null, [], new Dictionary<int, ApplicationUserInfo>(), []));

        var handler = new GetDashboardHandler(_repo.Object, db);
        var query = new GetDashboardQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Tasks.Should().BeEmpty();
        result.Stages.Should().BeEmpty();
        result.Team.Should().BeEmpty();
        result.Kpis.ActiveCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithJobs_ReturnsCorrectKpis()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();

        var stage1 = new JobStage { Id = 1, Name = "In Production", Code = "PROD", SortOrder = 1, Color = "#3b82f6", IsActive = true };
        var stage2 = new JobStage { Id = 2, Name = "QC Review", Code = "QC", SortOrder = 2, Color = "#22c55e", IsActive = true };
        var trackType = new TrackType
        {
            Id = 1,
            Name = "Production",
            Code = "PROD",
            IsDefault = true,
            Stages = [stage1, stage2],
        };

        var jobs = new List<Job>
        {
            new() { Id = 1, Title = "Job 1", JobNumber = "JOB-001", CurrentStage = stage1, CurrentStageId = 1, TrackTypeId = 1 },
            new() { Id = 2, Title = "Job 2", JobNumber = "JOB-002", CurrentStage = stage2, CurrentStageId = 2, TrackTypeId = 1,
                    DueDate = DateTimeOffset.UtcNow.AddDays(-1) },
        };

        _repo.Setup(r => r.GetDashboardDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardDataSet(trackType, jobs, new Dictionary<int, ApplicationUserInfo>(), []));

        var handler = new GetDashboardHandler(_repo.Object, db);

        // Act
        var result = await handler.Handle(new GetDashboardQuery(), CancellationToken.None);

        // Assert
        result.Kpis.ActiveCount.Should().Be(2);
        result.Kpis.OverdueCount.Should().Be(1);
        result.Stages.Should().HaveCount(2);
        result.Tasks.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WithAssignedJobs_ReturnsTeamMembers()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();

        var stage = new JobStage { Id = 1, Name = "Active", Code = "ACT", SortOrder = 1, Color = "#3b82f6", IsActive = true };
        var trackType = new TrackType { Id = 1, Name = "Production", Code = "PROD", IsDefault = true, Stages = [stage] };

        var users = new Dictionary<int, ApplicationUserInfo>
        {
            [10] = new(10, "DH", "Dan", "Hokanson", "#4f46e5"),
            [20] = new(20, "JD", "Jane", "Doe", "#ef4444"),
        };

        var jobs = new List<Job>
        {
            new() { Id = 1, Title = "Job A", JobNumber = "JOB-A", CurrentStage = stage, CurrentStageId = 1, TrackTypeId = 1, AssigneeId = 10 },
            new() { Id = 2, Title = "Job B", JobNumber = "JOB-B", CurrentStage = stage, CurrentStageId = 1, TrackTypeId = 1, AssigneeId = 10 },
            new() { Id = 3, Title = "Job C", JobNumber = "JOB-C", CurrentStage = stage, CurrentStageId = 1, TrackTypeId = 1, AssigneeId = 20 },
        };

        _repo.Setup(r => r.GetDashboardDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardDataSet(trackType, jobs, users, []));

        var handler = new GetDashboardHandler(_repo.Object, db);

        // Act
        var result = await handler.Handle(new GetDashboardQuery(), CancellationToken.None);

        // Assert
        result.Team.Should().HaveCount(2);
        result.Team.First().TaskCount.Should().Be(2);
        result.Team.First().Initials.Should().Be("DH");
    }
}
