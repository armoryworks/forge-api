using Bogus;
using FluentAssertions;
using Moq;
using Forge.Api.Features.Payments;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Payments;

public class CreatePaymentHandlerTests
{
    private readonly Mock<IPaymentRepository> _paymentRepo = new();
    private readonly Mock<ICustomerRepository> _customerRepo = new();
    private readonly Mock<IInvoiceRepository> _invoiceRepo = new();
    private readonly CreatePaymentHandler _handler;

    private readonly Faker _faker = new();

    public CreatePaymentHandlerTests()
    {
        _handler = new CreatePaymentHandler(_paymentRepo.Object, _customerRepo.Object, _invoiceRepo.Object);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesPaymentAndReturnsResult()
    {
        // Arrange
        var customerId = _faker.Random.Int(1, 100);
        var customer = new Customer { Id = customerId, Name = _faker.Company.CompanyName() };
        var paymentNumber = $"PMT-{_faker.Random.Int(1000, 9999)}";
        var amount = _faker.Finance.Amount(100, 5000);
        var paymentDate = DateTime.UtcNow;

        _customerRepo.Setup(r => r.FindAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _paymentRepo.Setup(r => r.GenerateNextPaymentNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(paymentNumber);

        var command = new CreatePaymentCommand(
            customerId, "Check", amount, paymentDate, "REF-001", "Test payment", null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.PaymentNumber.Should().Be(paymentNumber);
        result.CustomerId.Should().Be(customerId);
        result.Amount.Should().Be(amount);

        _paymentRepo.Verify(r => r.AddAsync(It.Is<Payment>(p =>
            p.PaymentNumber == paymentNumber &&
            p.CustomerId == customerId &&
            p.Amount == amount &&
            p.Method == PaymentMethod.Check
        ), It.IsAny<CancellationToken>()), Times.Once);

        _paymentRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithApplications_AppliesToInvoiceAndUpdatesStatus()
    {
        // Arrange
        var customerId = _faker.Random.Int(1, 100);
        var customer = new Customer { Id = customerId, Name = _faker.Company.CompanyName() };
        var invoiceId = _faker.Random.Int(1, 100);
        var invoiceTotal = 1000m;

        _customerRepo.Setup(r => r.FindAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _paymentRepo.Setup(r => r.GenerateNextPaymentNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("PMT-0001");

        var invoice = new Invoice
        {
            Id = invoiceId,
            InvoiceNumber = "INV-0001",
            CustomerId = customerId,
            TaxRate = 0,
            Status = InvoiceStatus.Sent,
        };
        invoice.Lines.Add(new InvoiceLine
        {
            Quantity = 1,
            UnitPrice = invoiceTotal,
            Description = "Widget",
            LineNumber = 1,
        });

        _invoiceRepo.Setup(r => r.FindWithDetailsAsync(invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);

        var applications = new List<CreatePaymentApplicationModel>
        {
            new(invoiceId, 500m),
        };

        var command = new CreatePaymentCommand(
            customerId, "Check", 500m, DateTime.UtcNow, null, null, applications);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.AppliedAmount.Should().Be(500m);
        result.UnappliedAmount.Should().Be(0m);
        invoice.Status.Should().Be(InvoiceStatus.PartiallyPaid);
    }

    [Fact]
    public async Task Handle_FullPayment_SetsInvoiceStatusToPaid()
    {
        // Arrange
        var customerId = _faker.Random.Int(1, 100);
        var customer = new Customer { Id = customerId, Name = _faker.Company.CompanyName() };
        var invoiceId = _faker.Random.Int(1, 100);

        _customerRepo.Setup(r => r.FindAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _paymentRepo.Setup(r => r.GenerateNextPaymentNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("PMT-0002");

        var invoice = new Invoice
        {
            Id = invoiceId,
            InvoiceNumber = "INV-0002",
            CustomerId = customerId,
            TaxRate = 0,
            Status = InvoiceStatus.Sent,
        };
        invoice.Lines.Add(new InvoiceLine
        {
            Quantity = 2,
            UnitPrice = 250m,
            Description = "Service",
            LineNumber = 1,
        });

        _invoiceRepo.Setup(r => r.FindWithDetailsAsync(invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);

        var applications = new List<CreatePaymentApplicationModel>
        {
            new(invoiceId, 500m),
        };

        var command = new CreatePaymentCommand(
            customerId, "CreditCard", 500m, DateTime.UtcNow, null, null, applications);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        invoice.Status.Should().Be(InvoiceStatus.Paid);
    }

    [Fact]
    public async Task Handle_CustomerNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var customerId = _faker.Random.Int(1, 100);

        _customerRepo.Setup(r => r.FindAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var command = new CreatePaymentCommand(
            customerId, "Check", 100m, DateTime.UtcNow, null, null, null);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*Customer {customerId}*");
    }

    // F-027 anti-drift guard: the over-application threshold must be exactly
    // invoice.BalanceDue (the canonical money formula), not a re-derivation that
    // could silently diverge. Uses a non-trivial balance — tax + an existing
    // partial payment — so a handler that forgot tax or prior applications fails.
    // BalanceDue = Subtotal(500) * (1 + 0.08) - alreadyPaid(140) = 400.
    [Fact]
    public async Task Handle_BalanceGuard_AcceptsExactlyInvoiceBalanceDue_AndRejectsOneCentMore()
    {
        var customerId = _faker.Random.Int(1, 100);
        var customer = new Customer { Id = customerId, Name = "Test" };
        var invoiceId = _faker.Random.Int(1, 100);

        _customerRepo.Setup(r => r.FindAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _paymentRepo.Setup(r => r.GenerateNextPaymentNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("PMT-0027");

        Invoice BuildInvoice()
        {
            var inv = new Invoice
            {
                Id = invoiceId,
                InvoiceNumber = "INV-0027",
                CustomerId = customerId,
                TaxRate = 0.08m,
                Status = InvoiceStatus.Sent,
            };
            inv.Lines.Add(new InvoiceLine { Quantity = 3, UnitPrice = 100m, Description = "A", LineNumber = 1 });
            inv.Lines.Add(new InvoiceLine { Quantity = 1, UnitPrice = 200m, Description = "B", LineNumber = 2 });
            inv.PaymentApplications.Add(new PaymentApplication { InvoiceId = invoiceId, Amount = 140m });
            return inv;
        }

        var probe = BuildInvoice();
        probe.BalanceDue.Should().Be(400m, "test arithmetic must track the canonical formula");

        // One cent over the canonical balance is rejected.
        var overInvoice = BuildInvoice();
        _invoiceRepo.Setup(r => r.FindWithDetailsAsync(invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(overInvoice);
        var overCommand = new CreatePaymentCommand(
            customerId, "Check", 400.01m, DateTime.UtcNow, null, null,
            [new(invoiceId, 400.01m)]);
        await FluentActions.Awaiting(() => _handler.Handle(overCommand, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds*balance*");

        // Exactly the canonical balance is accepted and clears the invoice.
        var exactInvoice = BuildInvoice();
        _invoiceRepo.Setup(r => r.FindWithDetailsAsync(invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(exactInvoice);
        var exactCommand = new CreatePaymentCommand(
            customerId, "Check", 400m, DateTime.UtcNow, null, null,
            [new(invoiceId, 400m)]);
        var result = await _handler.Handle(exactCommand, CancellationToken.None);
        result.AppliedAmount.Should().Be(400m);
        exactInvoice.Status.Should().Be(InvoiceStatus.Paid);
    }

    [Fact]
    public async Task Handle_ApplicationExceedsBalance_ThrowsInvalidOperationException()
    {
        // Arrange
        var customerId = _faker.Random.Int(1, 100);
        var customer = new Customer { Id = customerId, Name = "Test" };
        var invoiceId = _faker.Random.Int(1, 100);

        _customerRepo.Setup(r => r.FindAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _paymentRepo.Setup(r => r.GenerateNextPaymentNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("PMT-0003");

        var invoice = new Invoice
        {
            Id = invoiceId,
            InvoiceNumber = "INV-0003",
            CustomerId = customerId,
            TaxRate = 0,
            Status = InvoiceStatus.Sent,
        };
        invoice.Lines.Add(new InvoiceLine
        {
            Quantity = 1,
            UnitPrice = 100m,
            Description = "Item",
            LineNumber = 1,
        });

        _invoiceRepo.Setup(r => r.FindWithDetailsAsync(invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);

        var applications = new List<CreatePaymentApplicationModel>
        {
            new(invoiceId, 200m), // exceeds 100 balance
        };

        var command = new CreatePaymentCommand(
            customerId, "Check", 200m, DateTime.UtcNow, null, null, applications);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*exceeds*balance*");
    }
}
