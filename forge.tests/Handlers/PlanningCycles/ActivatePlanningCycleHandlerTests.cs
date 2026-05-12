using Bogus;
using FluentAssertions;
using Moq;
using Forge.Api.Features.PlanningCycles;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Tests.Handlers.PlanningCycles;

public class ActivatePlanningCycleHandlerTests
{
    private readonly Mock<IPlanningCycleRepository> _repo = new();
    private readonly ActivatePlanningCycleHandler _handler;

    private readonly Faker _faker = new();

    public ActivatePlanningCycleHandlerTests()
    {
        _handler = new ActivatePlanningCycleHandler(_repo.Object);
    }

    [Fact]
    public async Task Handle_PlanningStatusCycle_ActivatesSuccessfully()
    {
        // Arrange
        var cycleId = _faker.Random.Int(1, 100);
        var cycle = new PlanningCycle
        {
            Id = cycleId,
            Name = "Sprint 5",
            Status = PlanningCycleStatus.Planning,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(14),
        };

        _repo.Setup(r => r.FindAsync(cycleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cycle);

        var command = new ActivatePlanningCycleCommand(cycleId);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        cycle.Status.Should().Be(PlanningCycleStatus.Active);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CycleNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var cycleId = _faker.Random.Int(1, 100);

        _repo.Setup(r => r.FindAsync(cycleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlanningCycle?)null);

        var command = new ActivatePlanningCycleCommand(cycleId);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*Planning cycle {cycleId}*");
    }

    [Fact]
    public async Task Handle_ActiveCycle_ThrowsInvalidOperationException()
    {
        // Arrange
        var cycleId = _faker.Random.Int(1, 100);
        var cycle = new PlanningCycle
        {
            Id = cycleId,
            Name = "Sprint 3",
            Status = PlanningCycleStatus.Active,
            StartDate = DateTime.UtcNow.AddDays(-7),
            EndDate = DateTime.UtcNow.AddDays(7),
        };

        _repo.Setup(r => r.FindAsync(cycleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cycle);

        var command = new ActivatePlanningCycleCommand(cycleId);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot activate*Active*");
    }

    [Fact]
    public async Task Handle_CompletedCycle_ThrowsInvalidOperationException()
    {
        // Arrange
        var cycleId = _faker.Random.Int(1, 100);
        var cycle = new PlanningCycle
        {
            Id = cycleId,
            Name = "Sprint 1",
            Status = PlanningCycleStatus.Completed,
            StartDate = DateTime.UtcNow.AddDays(-21),
            EndDate = DateTime.UtcNow.AddDays(-7),
        };

        _repo.Setup(r => r.FindAsync(cycleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cycle);

        var command = new ActivatePlanningCycleCommand(cycleId);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot activate*Completed*");
    }
}
