using FluentAssertions;

using Forge.Api.Features.ScheduledTasks;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ScheduledTasks;

public class CreateScheduledTaskHandlerTests
{
    [Fact]
    public async Task Handle_ValidCommand_CreatesScheduledTask()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var trackType = new TrackType { Name = "Production", Code = "PROD", IsDefault = true };
        db.TrackTypes.Add(trackType);
        await db.SaveChangesAsync();

        var handler = new CreateScheduledTaskHandler(db);
        var command = new CreateScheduledTaskCommand(
            "Weekly Maintenance Check", "Run every Monday", trackType.Id, null, null, "0 0 * * MON");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Weekly Maintenance Check");
        result.TrackTypeName.Should().Be("Production");
        result.CronExpression.Should().Be("0 0 * * MON");
        result.IsActive.Should().BeTrue();

        db.ScheduledTasks.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_TrackTypeNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var handler = new CreateScheduledTaskHandler(db);
        var command = new CreateScheduledTaskCommand(
            "Task", null, 999, null, null, "0 0 * * *");

        // Act
        var act = () => handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*999*");
    }

    [Fact]
    public async Task Handle_WithOptionalFields_SetsFieldsCorrectly()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var trackType = new TrackType { Name = "Maintenance", Code = "MAINT", IsDefault = false };
        db.TrackTypes.Add(trackType);
        await db.SaveChangesAsync();

        var handler = new CreateScheduledTaskHandler(db);
        var command = new CreateScheduledTaskCommand(
            "Daily Inspection", "Daily machine check", trackType.Id, 42, 10, "0 8 * * *");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Description.Should().Be("Daily machine check");
        result.TrackTypeId.Should().Be(trackType.Id);

        var saved = db.ScheduledTasks.Single();
        saved.InternalProjectTypeId.Should().Be(42);
        saved.AssigneeId.Should().Be(10);
    }
}
