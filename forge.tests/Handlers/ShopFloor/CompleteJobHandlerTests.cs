using Bogus;
using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ShopFloor;

public class CompleteJobHandlerTests
{
    private readonly CompleteJobHandler _handler;
    private readonly AppDbContext _db;
    private readonly Faker _faker = new();

    public CompleteJobHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new CompleteJobHandler(_db, new SystemClock());
    }

    [Fact]
    public async Task Handle_ValidJob_MovesToLastStageAndSetsCompletedAt()
    {
        // Arrange
        var trackType = new TrackType { Name = "Production", Code = "PROD", IsActive = true };
        _db.TrackTypes.Add(trackType);
        await _db.SaveChangesAsync();

        var firstStage = new JobStage
        {
            TrackTypeId = trackType.Id,
            Name = "In Production",
            Code = "in_production",
            SortOrder = 1,
            IsActive = true,
        };
        var lastStage = new JobStage
        {
            TrackTypeId = trackType.Id,
            Name = "Payment Received",
            Code = "payment_received",
            SortOrder = 10,
            IsActive = true,
        };
        _db.JobStages.AddRange(firstStage, lastStage);
        await _db.SaveChangesAsync();

        var job = new Job
        {
            JobNumber = "JOB-0001",
            Title = _faker.Commerce.ProductName(),
            TrackTypeId = trackType.Id,
            CurrentStageId = firstStage.Id,
            Priority = JobPriority.Normal,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        var command = new CompleteJobCommand(job.Id);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var updatedJob = await _db.Jobs.FirstAsync(j => j.Id == job.Id);
        updatedJob.CurrentStageId.Should().Be(lastStage.Id);
        updatedJob.CompletedDate.Should().NotBeNull();
        updatedJob.CompletedDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_AlreadyCompletedJob_StillMovesToLastStage()
    {
        // Arrange — a job that already has CompletedDate set but is not on last stage
        var trackType = new TrackType { Name = "R&D", Code = "RD", IsActive = true };
        _db.TrackTypes.Add(trackType);
        await _db.SaveChangesAsync();

        var stage1 = new JobStage
        {
            TrackTypeId = trackType.Id,
            Name = "Design",
            Code = "design",
            SortOrder = 1,
            IsActive = true,
        };
        var stage2 = new JobStage
        {
            TrackTypeId = trackType.Id,
            Name = "Complete",
            Code = "complete",
            SortOrder = 2,
            IsActive = true,
        };
        _db.JobStages.AddRange(stage1, stage2);
        await _db.SaveChangesAsync();

        var job = new Job
        {
            JobNumber = "JOB-0002",
            Title = "Already Done",
            TrackTypeId = trackType.Id,
            CurrentStageId = stage1.Id,
            CompletedDate = DateTime.UtcNow.AddDays(-1),
            Priority = JobPriority.Normal,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        var command = new CompleteJobCommand(job.Id);

        // Act — handler does not check for already completed, it just moves and sets date
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var updatedJob = await _db.Jobs.FirstAsync(j => j.Id == job.Id);
        updatedJob.CurrentStageId.Should().Be(stage2.Id);
        updatedJob.CompletedDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_NonExistentJob_ThrowsKeyNotFoundException()
    {
        // Arrange
        var command = new CompleteJobCommand(99999);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*99999*");
    }
}
