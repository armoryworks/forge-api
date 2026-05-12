using FluentAssertions;
using Moq;

using Forge.Api.Features.RecurringOrders;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.RecurringOrders;

public class CreateRecurringOrderHandlerTests
{
    private readonly Mock<IRecurringOrderRepository> _repo = new();
    private readonly Mock<ICustomerRepository> _customerRepo = new();

    [Fact]
    public async Task Handle_ValidCommand_CreatesRecurringOrderWithLines()
    {
        // Arrange
        var customer = new Customer { Id = 1, Name = "Acme Corp", IsActive = true };
        _customerRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        var nextDate = DateTimeOffset.UtcNow.AddDays(30);
        var command = new CreateRecurringOrderCommand(
            "Monthly Widgets", 1, null, 30, nextDate, "Repeat monthly",
            [
                new CreateRecurringOrderLineModel(10, "Widget A", 100, 5.50m),
                new CreateRecurringOrderLineModel(20, "Widget B", 50, 12.00m),
            ]);

        RecurringOrder? captured = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<RecurringOrder>(), It.IsAny<CancellationToken>()))
            .Callback<RecurringOrder, CancellationToken>((ro, _) => captured = ro);

        var handler = new CreateRecurringOrderHandler(_repo.Object, _customerRepo.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Monthly Widgets");
        result.CustomerName.Should().Be("Acme Corp");
        result.IntervalDays.Should().Be(30);
        result.LineCount.Should().Be(2);

        captured.Should().NotBeNull();
        captured!.Lines.Should().HaveCount(2);
        captured.Lines.First().LineNumber.Should().Be(1);
        captured.Lines.Last().LineNumber.Should().Be(2);
    }

    [Fact]
    public async Task Handle_CustomerNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        _customerRepo.Setup(r => r.FindAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var command = new CreateRecurringOrderCommand(
            "Test Order", 999, null, 7, DateTimeOffset.UtcNow, null,
            [new CreateRecurringOrderLineModel(1, "Part", 10, 1.00m)]);

        var handler = new CreateRecurringOrderHandler(_repo.Object, _customerRepo.Object);

        // Act
        var act = () => handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*999*");
    }

    [Fact]
    public async Task Handle_LinesNumberedSequentially()
    {
        // Arrange
        var customer = new Customer { Id = 1, Name = "Test Co", IsActive = true };
        _customerRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        RecurringOrder? captured = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<RecurringOrder>(), It.IsAny<CancellationToken>()))
            .Callback<RecurringOrder, CancellationToken>((ro, _) => captured = ro);

        var command = new CreateRecurringOrderCommand(
            "Multi-line", 1, null, 14, DateTimeOffset.UtcNow, null,
            [
                new CreateRecurringOrderLineModel(1, "A", 1, 1m),
                new CreateRecurringOrderLineModel(2, "B", 2, 2m),
                new CreateRecurringOrderLineModel(3, "C", 3, 3m),
            ]);

        var handler = new CreateRecurringOrderHandler(_repo.Object, _customerRepo.Object);

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        captured!.Lines.Select(l => l.LineNumber).Should().BeEquivalentTo([1, 2, 3]);
    }
}
