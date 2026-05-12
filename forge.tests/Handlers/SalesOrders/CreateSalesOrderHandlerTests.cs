using Bogus;
using FluentAssertions;
using Moq;
using Forge.Api.Features.SalesOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.SalesOrders;

public class CreateSalesOrderHandlerTests
{
    private readonly Mock<ISalesOrderRepository> _orderRepo = new();
    private readonly Mock<ICustomerRepository> _customerRepo = new();
    private readonly Mock<IPartRepository> _partRepo = new();
    private readonly Mock<IBarcodeService> _barcodeService = new();
    private readonly CreateSalesOrderHandler _handler;

    private readonly Faker _faker = new();

    public CreateSalesOrderHandlerTests()
    {
        // WU-09 regression: passthrough default returns an active Part so the
        // active-check on PartId-bearing lines does not throw. Tests that
        // exercise part-active behavior override per-test.
        _partRepo
            .Setup(r => r.FindAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => new Part { Id = id, Status = PartStatus.Active });

        _handler = new CreateSalesOrderHandler(
            _orderRepo.Object, _customerRepo.Object, _partRepo.Object, _barcodeService.Object);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesOrderAndReturnsListItem()
    {
        // Arrange
        var customerId = _faker.Random.Int(1, 100);
        var orderNumber = $"SO-{_faker.Random.Int(1000, 9999)}";
        var customer = new Customer { Id = customerId, Name = _faker.Company.CompanyName() };

        _customerRepo.Setup(r => r.FindAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _orderRepo.Setup(r => r.GenerateNextOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(orderNumber);

        var command = new CreateSalesOrderCommand(
            customerId, null, null, null, null, null, "CUST-PO-123", "Test notes", 0.08m,
            [new CreateSalesOrderLineModel(null, "Widget", 10, 25.00m, null)]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.OrderNumber.Should().Be(orderNumber);
        result.CustomerId.Should().Be(customerId);
        result.CustomerName.Should().Be(customer.Name);
        result.LineCount.Should().Be(1);
        result.Total.Should().Be(250.00m);
        result.CustomerPO.Should().Be("CUST-PO-123");

        _orderRepo.Verify(r => r.AddAsync(It.Is<SalesOrder>(so =>
            so.OrderNumber == orderNumber &&
            so.CustomerId == customerId &&
            so.TaxRate == 0.08m &&
            so.CustomerPO == "CUST-PO-123" &&
            so.Lines.Count == 1
        ), It.IsAny<CancellationToken>()), Times.Once);

        _orderRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CustomerNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        _customerRepo.Setup(r => r.FindAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var command = new CreateSalesOrderCommand(
            999, null, null, null, null, null, null, null, 0m,
            [new CreateSalesOrderLineModel(null, "Item", 1, 10m, null)]);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*Customer 999*");
    }

    [Fact]
    public async Task Handle_MultipleLines_CalculatesTotalCorrectly()
    {
        // Arrange
        var customerId = 1;
        var customer = new Customer { Id = customerId, Name = "Test Customer" };

        _customerRepo.Setup(r => r.FindAsync(customerId, It.IsAny<CancellationToken>())).ReturnsAsync(customer);
        _orderRepo.Setup(r => r.GenerateNextOrderNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("SO-0001");

        var command = new CreateSalesOrderCommand(
            customerId, null, null, null, null, null, null, null, 0m,
            [
                new CreateSalesOrderLineModel(null, "Widget A", 5, 10m, null),
                new CreateSalesOrderLineModel(null, "Widget B", 3, 20m, null),
            ]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Total.Should().Be(110m); // (5*10) + (3*20) = 110
        result.LineCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_AssignsSequentialLineNumbers()
    {
        // Arrange
        var customerId = 1;
        var customer = new Customer { Id = customerId, Name = "Test" };

        _customerRepo.Setup(r => r.FindAsync(customerId, It.IsAny<CancellationToken>())).ReturnsAsync(customer);
        _orderRepo.Setup(r => r.GenerateNextOrderNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("SO-0001");

        var command = new CreateSalesOrderCommand(
            customerId, null, null, null, null, null, null, null, 0m,
            [
                new CreateSalesOrderLineModel(null, "Line 1", 1, 10m, null),
                new CreateSalesOrderLineModel(null, "Line 2", 1, 20m, null),
                new CreateSalesOrderLineModel(null, "Line 3", 1, 30m, null),
            ]);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _orderRepo.Verify(r => r.AddAsync(It.Is<SalesOrder>(so =>
            so.Lines.Count == 3 &&
            so.Lines.ElementAt(0).LineNumber == 1 &&
            so.Lines.ElementAt(1).LineNumber == 2 &&
            so.Lines.ElementAt(2).LineNumber == 3
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithCreditTerms_ParsesEnumCorrectly()
    {
        // Arrange
        var customerId = 1;
        var customer = new Customer { Id = customerId, Name = "Test" };

        _customerRepo.Setup(r => r.FindAsync(customerId, It.IsAny<CancellationToken>())).ReturnsAsync(customer);
        _orderRepo.Setup(r => r.GenerateNextOrderNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("SO-0001");

        var command = new CreateSalesOrderCommand(
            customerId, null, null, null, "Net30", null, null, null, 0m,
            [new CreateSalesOrderLineModel(null, "Item", 1, 100m, null)]);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _orderRepo.Verify(r => r.AddAsync(It.Is<SalesOrder>(so =>
            so.CreditTerms == Core.Enums.CreditTerms.Net30
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NullCreditTerms_SetsNull()
    {
        // Arrange
        var customerId = 1;
        var customer = new Customer { Id = customerId, Name = "Test" };

        _customerRepo.Setup(r => r.FindAsync(customerId, It.IsAny<CancellationToken>())).ReturnsAsync(customer);
        _orderRepo.Setup(r => r.GenerateNextOrderNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("SO-0001");

        var command = new CreateSalesOrderCommand(
            customerId, null, null, null, null, null, null, null, 0m,
            [new CreateSalesOrderLineModel(null, "Item", 1, 100m, null)]);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _orderRepo.Verify(r => r.AddAsync(It.Is<SalesOrder>(so =>
            so.CreditTerms == null
        ), It.IsAny<CancellationToken>()), Times.Once);
    }
}
