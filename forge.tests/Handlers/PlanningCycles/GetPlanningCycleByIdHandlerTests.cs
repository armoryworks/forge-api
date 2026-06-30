using FluentAssertions;
using Moq;

using Forge.Api.Features.PlanningCycles;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PlanningCycles;

public class GetPlanningCycleByIdHandlerTests
{
    private readonly Mock<IPlanningCycleRepository> _repo = new();
    private readonly AppDbContext _db;
    private readonly GetPlanningCycleByIdHandler _handler;

    public GetPlanningCycleByIdHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new GetPlanningCycleByIdHandler(_repo.Object, _db);
    }

    [Fact]
    public async Task Handle_PopulatesAssigneeName_FromAssignedUser_AndNullForUnassigned()
    {
        // Arrange — one assigned user, plus an entry with no assignee.
        var user = new ApplicationUser
        {
            UserName = "dhartman@test.com", Email = "dhartman@test.com",
            FirstName = "Daniel", LastName = "Hartman", Initials = "DH", AvatarColor = "#94a3b8",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var stage = new JobStage { Name = "In Production", Code = "in-prod", Color = "#22c55e" };

        var assignedJob = new Job
        {
            Id = 1, JobNumber = "J-100", Title = "Assigned job",
            AssigneeId = user.Id, Priority = JobPriority.Normal, CurrentStage = stage,
        };
        var unassignedJob = new Job
        {
            Id = 2, JobNumber = "J-200", Title = "Unassigned job",
            AssigneeId = null, Priority = JobPriority.Normal, CurrentStage = stage,
        };

        var cycle = new PlanningCycle
        {
            Id = 42, Name = "Sprint 7", Status = PlanningCycleStatus.Active,
            StartDate = DateTimeOffset.UtcNow, EndDate = DateTimeOffset.UtcNow.AddDays(14),
            DurationDays = 14,
            Entries =
            [
                new PlanningCycleEntry { Id = 11, JobId = 1, Job = assignedJob, SortOrder = 0 },
                new PlanningCycleEntry { Id = 12, JobId = 2, Job = unassignedJob, SortOrder = 1 },
            ],
        };

        _repo.Setup(r => r.FindWithDetailsAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cycle);

        // Act
        var result = await _handler.Handle(new GetPlanningCycleByIdQuery(42), CancellationToken.None);

        // Assert — assigned entry shows "Last, First"; unassigned stays null.
        var assignedEntry = result.Entries.Single(e => e.JobId == 1);
        var unassignedEntry = result.Entries.Single(e => e.JobId == 2);

        assignedEntry.AssigneeName.Should().Be("Hartman, Daniel");
        unassignedEntry.AssigneeName.Should().BeNull();
    }
}
