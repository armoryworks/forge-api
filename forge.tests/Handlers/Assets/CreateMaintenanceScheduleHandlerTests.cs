using Bogus;
using FluentAssertions;
using Forge.Api.Features.Assets;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Assets;

public class CreateMaintenanceScheduleHandlerTests
{
    private readonly Faker _faker = new();

    [Fact]
    public async Task Handle_ValidCommand_CreatesScheduleAndReturnsResult()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();

        var asset = new Asset { Name = "CNC Mill", AssetType = Core.Enums.AssetType.Machine };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        var handler = new CreateMaintenanceScheduleHandler(db);
        var nextDue = DateTime.UtcNow.AddDays(30);

        var data = new CreateMaintenanceScheduleRequestModel(
            asset.Id, "Oil Change", "Change spindle oil", 30, null, nextDue);

        var command = new CreateMaintenanceScheduleCommand(data);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.AssetId.Should().Be(asset.Id);
        result.AssetName.Should().Be("CNC Mill");
        result.Title.Should().Be("Oil Change");
        result.Description.Should().Be("Change spindle oil");
        result.IntervalDays.Should().Be(30);
        result.NextDueAt.Should().Be(nextDue);
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_SetsNextDueAtFromRequest()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();

        var asset = new Asset { Name = "Lathe", AssetType = Core.Enums.AssetType.Machine };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        var handler = new CreateMaintenanceScheduleHandler(db);
        var nextDue = DateTime.UtcNow.AddDays(7);

        var data = new CreateMaintenanceScheduleRequestModel(
            asset.Id, "Belt Check", null, 7, null, nextDue);

        var command = new CreateMaintenanceScheduleCommand(data);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.NextDueAt.Should().Be(nextDue);
        result.IntervalDays.Should().Be(7);
    }

    [Fact]
    public async Task Handle_AssetNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();

        var handler = new CreateMaintenanceScheduleHandler(db);
        var nonExistentId = _faker.Random.Int(900, 999);

        var data = new CreateMaintenanceScheduleRequestModel(
            nonExistentId, "Test", null, 30, null, DateTime.UtcNow.AddDays(30));

        var command = new CreateMaintenanceScheduleCommand(data);

        // Act
        var act = () => handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*Asset {nonExistentId}*");
    }

    [Fact]
    public async Task Handle_WithIntervalHours_StoresCorrectly()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();

        var asset = new Asset { Name = "Press", AssetType = Core.Enums.AssetType.Machine };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        var handler = new CreateMaintenanceScheduleHandler(db);
        var nextDue = DateTime.UtcNow.AddDays(90);

        var data = new CreateMaintenanceScheduleRequestModel(
            asset.Id, "Filter Replacement", "Replace hydraulic filter", 90, 500m, nextDue);

        var command = new CreateMaintenanceScheduleCommand(data);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IntervalDays.Should().Be(90);
        result.IntervalHours.Should().Be(500m);
    }

    [Fact]
    public async Task Handle_OverdueSchedule_SetsIsOverdueTrue()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();

        var asset = new Asset { Name = "Compressor", AssetType = Core.Enums.AssetType.Machine };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        var handler = new CreateMaintenanceScheduleHandler(db);
        var pastDue = DateTime.UtcNow.AddDays(-5);

        var data = new CreateMaintenanceScheduleRequestModel(
            asset.Id, "Air Filter", null, 30, null, pastDue);

        var command = new CreateMaintenanceScheduleCommand(data);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsOverdue.Should().BeTrue();
    }
}
