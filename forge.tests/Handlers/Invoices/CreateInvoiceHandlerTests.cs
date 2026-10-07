using Bogus;
using FluentAssertions;
using Moq;
using Forge.Api.Features.Invoices;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Invoices;

public class CreateInvoiceHandlerTests
{
    private readonly Mock<IInvoiceRepository> _invoiceRepo = new();
    private readonly Mock<ICustomerRepository> _customerRepo = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly CreateInvoiceHandler _handler;

    private readonly Faker _faker = new();

    public CreateInvoiceHandlerTests()
    {
        // Real in-memory DbContext rather than mocking — handler uses it to
        // resolve SO.CustomerPO when SalesOrderId is set. Tests that don't
        // pass SalesOrderId hit an empty SalesOrders set; harmless.
        _handler = new CreateInvoiceHandler(_invoiceRepo.Object, _customerRepo.Object, _db);
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

    private async Task<(Customer Customer, SalesOrder Order, Part Part)> SeedOrderAsync(string orderNumber = "SO-00042")
    {
        var customer = new Customer { Name = "Order Customer" };
        var part = new Part { PartNumber = "PN-1001", Name = "Bracket" };
        _db.Customers.Add(customer);
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        var order = new SalesOrder { OrderNumber = orderNumber, CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();

        _customerRepo.Setup(r => r.FindAsync(customer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(customer);
        _invoiceRepo.Setup(r => r.GenerateNextInvoiceNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("INV-0100");
        return (customer, order, part);
    }

    private async Task<Shipment> SeedShipmentAsync(SalesOrder order, Part part, decimal quantity, ShipmentStatus status)
    {
        var shipment = new Shipment
        {
            ShipmentNumber = $"SHP-{_db.Shipments.Count() + 1:00000}",
            SalesOrderId = order.Id,
            Status = status,
            Lines = [new ShipmentLine { PartId = part.Id, Quantity = quantity }],
        };
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();
        return shipment;
    }

    private static CreateInvoiceCommand OrderCommand(int customerId, int? salesOrderId, int? shipmentId, params CreateInvoiceLineModel[] lines) =>
        new(customerId, salesOrderId, shipmentId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), null, 0m, null, [.. lines]);

    [Fact]
    public async Task Handle_SalesOrderForAnotherCustomer_ThrowsFieldError()
    {
        var (_, order, _) = await SeedOrderAsync();
        var other = new Customer { Id = 900, Name = "Someone Else" };
        _customerRepo.Setup(r => r.FindAsync(other.Id, It.IsAny<CancellationToken>())).ReturnsAsync(other);

        var act = () => _handler.Handle(
            OrderCommand(other.Id, order.Id, null, new CreateInvoiceLineModel(null, "Freight", 1m, 10m)), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<FluentValidation.ValidationException>()).Which;
        ex.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateInvoiceCommand.SalesOrderId)
            && e.ErrorMessage.Contains("SO-00042"));
    }

    [Fact]
    public async Task Handle_ShipmentFromAnotherOrder_ThrowsFieldError()
    {
        var (customer, order, part) = await SeedOrderAsync();
        var secondOrder = new SalesOrder { OrderNumber = "SO-00043", CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed };
        _db.SalesOrders.Add(secondOrder);
        await _db.SaveChangesAsync();
        var shipment = await SeedShipmentAsync(secondOrder, part, 5m, ShipmentStatus.Shipped);

        var act = () => _handler.Handle(
            OrderCommand(customer.Id, order.Id, shipment.Id, new CreateInvoiceLineModel(null, "Freight", 1m, 10m)), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<FluentValidation.ValidationException>()).Which;
        ex.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateInvoiceCommand.ShipmentId)
            && e.ErrorMessage.Contains("SO-00043"));
    }

    [Fact]
    public async Task Handle_ShipmentForAnotherCustomer_ThrowsFieldError()
    {
        var (_, order, part) = await SeedOrderAsync();
        var shipment = await SeedShipmentAsync(order, part, 5m, ShipmentStatus.Shipped);
        var other = new Customer { Id = 901, Name = "Someone Else" };
        _customerRepo.Setup(r => r.FindAsync(other.Id, It.IsAny<CancellationToken>())).ReturnsAsync(other);

        var act = () => _handler.Handle(
            OrderCommand(other.Id, null, shipment.Id, new CreateInvoiceLineModel(null, "Freight", 1m, 10m)), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<FluentValidation.ValidationException>()).Which;
        ex.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateInvoiceCommand.ShipmentId));
    }

    [Fact]
    public async Task Handle_ShipmentAlreadyInvoiced_NamesTheShipmentAndInvoice()
    {
        var (customer, order, part) = await SeedOrderAsync();
        var shipment = await SeedShipmentAsync(order, part, 5m, ShipmentStatus.Shipped);
        _db.Invoices.Add(new Invoice { InvoiceNumber = "INV-0007", CustomerId = customer.Id, SalesOrderId = order.Id, ShipmentId = shipment.Id });
        await _db.SaveChangesAsync();

        var act = () => _handler.Handle(
            OrderCommand(customer.Id, order.Id, shipment.Id, new CreateInvoiceLineModel(null, "Freight", 1m, 10m)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Shipment {shipment.ShipmentNumber} is already on invoice INV-0007. Each shipment can be invoiced once.");
    }

    [Fact]
    public async Task Handle_OverInvoicing_NamesThePartNumber()
    {
        var (customer, order, part) = await SeedOrderAsync();
        await SeedShipmentAsync(order, part, 5m, ShipmentStatus.Delivered);
        _db.Invoices.Add(new Invoice
        {
            InvoiceNumber = "INV-0008",
            CustomerId = customer.Id,
            SalesOrderId = order.Id,
            Lines = [new InvoiceLine { PartId = part.Id, Description = "Bracket", Quantity = 2m, UnitPrice = 1m, LineNumber = 1 }],
        });
        await _db.SaveChangesAsync();

        var act = () => _handler.Handle(
            OrderCommand(customer.Id, order.Id, null, new CreateInvoiceLineModel(part.Id, "Bracket", 4m, 1m)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Can't invoice 4 of PN-1001: 5 shipped, 2 already invoiced, 3 left to invoice.");
    }

    [Fact]
    public async Task Handle_DeliveredShipmentCountsAsShipped()
    {
        var (customer, order, part) = await SeedOrderAsync();
        await SeedShipmentAsync(order, part, 5m, ShipmentStatus.Delivered);

        var result = await _handler.Handle(
            OrderCommand(customer.Id, order.Id, null, new CreateInvoiceLineModel(part.Id, "Bracket", 5m, 2m)), CancellationToken.None);

        result.Total.Should().Be(10m);
    }

    [Fact]
    public async Task Handle_UnknownSalesOrder_ThrowsKeyNotFound()
    {
        var (customer, _, _) = await SeedOrderAsync();

        var act = () => _handler.Handle(
            OrderCommand(customer.Id, 9999, null, new CreateInvoiceLineModel(null, "Freight", 1m, 10m)), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*Sales order 9999*");
    }
}
