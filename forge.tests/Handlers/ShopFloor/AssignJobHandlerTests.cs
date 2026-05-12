using Bogus;
using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ShopFloor;

public class AssignJobHandlerTests
{
    private readonly AssignJobHandler _handler;
    private readonly AppDbContext _db;
    private readonly Faker _faker = new();

    public AssignJobHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new AssignJobHandler(_db);
    }

    [Fact]
    public async Task Handle_ValidAssignment_SetsAssigneeId()
    {
        // Arrange
        var trackType = new TrackType { Name = "Production", Code = "PROD", IsActive = true };
        _db.TrackTypes.Add(trackType);
        await _db.SaveChangesAsync();

        var stage = new JobStage
        {
            TrackTypeId = trackType.Id,
            Name = "In Production",
            Code = "in_production",
            SortOrder = 1,
            IsActive = true,
        };
        _db.JobStages.Add(stage);
        await _db.SaveChangesAsync();

        var user = new ApplicationUser
        {
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
            UserName = _faker.Internet.Email(),
            Email = _faker.Internet.Email(),
            IsActive = true,
        };
        _db.Users.Add(user);

        var job = new Job
        {
            JobNumber = "JOB-0001",
            Title = _faker.Commerce.ProductName(),
            TrackTypeId = trackType.Id,
            CurrentStageId = stage.Id,
            Priority = JobPriority.Normal,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        var command = new AssignJobCommand(job.Id, user.Id);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var updatedJob = await _db.Jobs.FirstAsync(j => j.Id == job.Id);
        updatedJob.AssigneeId.Should().Be(user.Id);
    }

    [Fact]
    public async Task Handle_NonExistentJob_ThrowsKeyNotFoundException()
    {
        // Arrange
        var command = new AssignJobCommand(99999, 1);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*99999*");
    }

    [Fact]
    public async Task Handle_ReassignJob_UpdatesAssigneeId()
    {
        // Arrange
        var trackType = new TrackType { Name = "Production", Code = "PROD", IsActive = true };
        _db.TrackTypes.Add(trackType);
        await _db.SaveChangesAsync();

        var stage = new JobStage
        {
            TrackTypeId = trackType.Id,
            Name = "In Production",
            Code = "in_production",
            SortOrder = 1,
            IsActive = true,
        };
        _db.JobStages.Add(stage);
        await _db.SaveChangesAsync();

        var user1 = new ApplicationUser
        {
            FirstName = "Alice",
            LastName = "Smith",
            UserName = "alice@example.com",
            Email = "alice@example.com",
            IsActive = true,
        };
        var user2 = new ApplicationUser
        {
            FirstName = "Bob",
            LastName = "Jones",
            UserName = "bob@example.com",
            Email = "bob@example.com",
            IsActive = true,
        };
        _db.Users.AddRange(user1, user2);

        var job = new Job
        {
            JobNumber = "JOB-0002",
            Title = "Reassign Test",
            TrackTypeId = trackType.Id,
            CurrentStageId = stage.Id,
            AssigneeId = user1.Id,
            Priority = JobPriority.Normal,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        // Reassign the job ID after save so it gets the generated value
        job.AssigneeId = user1.Id;
        await _db.SaveChangesAsync();

        var command = new AssignJobCommand(job.Id, user2.Id);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var updatedJob = await _db.Jobs.FirstAsync(j => j.Id == job.Id);
        updatedJob.AssigneeId.Should().Be(user2.Id);
    }
}
