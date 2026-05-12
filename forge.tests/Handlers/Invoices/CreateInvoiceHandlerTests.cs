using Bogus;
using FluentAssertions;
using Moq;
using Forge.Api.Features.Invoices;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Invoices;

public class CreateInvoiceHandlerTests
{
    private readonly Mock<IInvoiceRepository> _invoiceRepo = new();
    private readonly Mock<ICustomerRepository> _customerRepo = new();
    private readonly CreateInvoiceHandler _handler;

    private readonly Faker _faker = new();

    public CreateInvoiceHandlerTests()
    {
        // Real in-memory DbContext rather than mocking — handler uses it to
        // resolve SO.CustomerPO when SalesOrderId is set. Tests that don't
        // pass SalesOrderId hit an empty SalesOrders set; harmless.
        var db = TestDbContextFactory.Create();
        _handler = new CreateInvoiceHandler(_invoiceRepo.Object, _customerRepo.Object, db);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesInvoiceWithLinesAndReturnsResult()
    {
        // Arrange
        var customerId = _faker.Random.Int(1, 100);
        var customer = new Customer { Id = customerId, Name = _faker.Company.CompanyName() };
        var invoiceNumber = $"INV-{_faker.Random.Int(1000, 9999)}";
        var invoiceDate = DateTime.UtcNow;
        var dueDate = invoiceDate.AddDays(30);

        _customerRepo.Setup(r => r.FindAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _invoiceRepo.Setup(r => r.GenerateNextInvoiceNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoiceNumber);

        var lines = new List<CreateInvoiceLineModel>
        {
            new(null, "Widget A", 2, 50m),
            new(null, "Widget B", 1, 100m),
        };

        var command = new CreateInvoiceCommand(
            customerId, null, null, invoiceDate, dueDate, null, 0.08m, "Test invoice", lines);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.InvoiceNumber.Should().Be(invoiceNumber);
        result.CustomerId.Should().Be(customerId);
        result.CustomerName.Should().Be(customer.Name);

        // Total should be (2*50 + 1*100) * 1.08 = 200 * 1.08 = 216
        result.Total.Should().Be(216m);
        result.AmountPaid.Should().Be(0m);
        result.BalanceDue.Should().Be(216m);

        _invoiceRepo.Verify(r => r.AddAsync(It.Is<Invoice>(i =>
            i.InvoiceNumber == invoiceNumber &&
            i.CustomerId == customerId &&
            i.Lines.Count == 2 &&
            i.TaxRate == 0.08m
        ), It.IsAny<CancellationToken>()), Times.Once);

        _invoiceRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AssignsSequentialLineNumbers()
    {
        // Arrange
        var customerId = _faker.Random.Int(1, 100);
        var customer = new Customer { Id = customerId, Name = "Test Corp" };

        _customerRepo.Setup(r => r.FindAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _invoiceRepo.Setup(r => r.GenerateNextInvoiceNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("INV-0001");

        var lines = new List<CreateInvoiceLineModel>
        {
            new(null, "Line 1", 1, 10m),
            new(null, "Line 2", 1, 20m),
            new(null, "Line 3", 1, 30m),
        };

        var command = new CreateInvoiceCommand(
            customerId, null, null, DateTime.UtcNow, DateTime.UtcNow.AddDays(30), null, 0m, null, lines);

        Invoice? capturedInvoice = null;
        _invoiceRepo.Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Callback<Invoice, CancellationToken>((inv, _) => capturedInvoice = inv);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        capturedInvoice.Should().NotBeNull();
        capturedInvoice!.Lines.Select(l => l.LineNumber).Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public async Task Handle_CustomerNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var customerId = _faker.Random.Int(1, 100);

        _customerRepo.Setup(r => r.FindAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var lines = new List<CreateInvoiceLineModel>
        {
            new(null, "Widget", 1, 100m),
        };

        var command = new CreateInvoiceCommand(
            customerId, null, null, DateTime.UtcNow, DateTime.UtcNow.AddDays(30), null, 0m, null, lines);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*Customer {customerId}*");
    }

    [Fact]
    public async Task Handle_WithCreditTerms_ParsesEnumCorrectly()
    {
        // Arrange
        var customerId = _faker.Random.Int(1, 100);
        var customer = new Customer { Id = customerId, Name = "Test" };

        _customerRepo.Setup(r => r.FindAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _invoiceRepo.Setup(r => r.GenerateNextInvoiceNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("INV-0010");

        var lines = new List<CreateInvoiceLineModel> { new(null, "Item", 1, 50m) };

        var command = new CreateInvoiceCommand(
            customerId, null, null, DateTime.UtcNow, DateTime.UtcNow.AddDays(30),
            "Net30", 0m, null, lines);

        Invoice? capturedInvoice = null;
        _invoiceRepo.Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Callback<Invoice, CancellationToken>((inv, _) => capturedInvoice = inv);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        capturedInvoice.Should().NotBeNull();
        capturedInvoice!.CreditTerms.Should().Be(CreditTerms.Net30);
    }
}
