using FluentAssertions;
using FluentValidation;
using Moq;

using Forge.Api.Features.Invoices;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Invoices;

public class CreateInvoiceCreditTermsTests
{
    private static readonly DateTimeOffset InvoiceDate = new(2026, 10, 19, 0, 0, 0, TimeSpan.FromHours(-6));

    private readonly Mock<IInvoiceRepository> _invoiceRepo = new();
    private readonly Mock<ICustomerRepository> _customerRepo = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly CreateInvoiceHandler _handler;
    private readonly CreateInvoiceValidator _validator = new();
    private Invoice? _saved;

    public CreateInvoiceCreditTermsTests()
    {
        _invoiceRepo.Setup(r => r.GenerateNextInvoiceNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("INV-0200");
        _invoiceRepo.Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Callback<Invoice, CancellationToken>((inv, _) => _saved = inv);
        _handler = new CreateInvoiceHandler(_invoiceRepo.Object, _customerRepo.Object, _db);
    }

    private static CreateInvoiceCommand Command(int customerId, int? salesOrderId, int dueInDays, string? terms) =>
        new(customerId, salesOrderId, null, InvoiceDate, InvoiceDate.AddDays(dueInDays), terms, 0m, null,
            [new CreateInvoiceLineModel(null, "Freight", 1m, 10m)]);

    private async Task<Customer> AddCustomerAsync(CreditTerms? defaultTerms = null)
    {
        var customer = new Customer { Name = "Terms Customer", DefaultCreditTerms = defaultTerms };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        _customerRepo.Setup(r => r.FindAsync(customer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(customer);
        return customer;
    }

    private async Task<SalesOrder> AddOrderAsync(int customerId, CreditTerms? terms)
    {
        var order = new SalesOrder { OrderNumber = "SO-00300", CustomerId = customerId, Status = SalesOrderStatus.Confirmed, CreditTerms = terms };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    [Theory]
    [InlineData("Net30")]
    [InlineData("Net 30")]
    [InlineData("net-30")]
    public void Validator_accepts_the_spellings_people_type(string terms)
    {
        _validator.Validate(Command(1, null, 30, terms)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("Net 31")]
    [InlineData("2")]
    [InlineData("Monthly")]
    public void Validator_rejects_unknown_terms_instead_of_failing_with_a_500(string terms)
    {
        var result = _validator.Validate(Command(1, null, 30, terms));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == CreditTermsExtensions.AllowedValuesMessage);
    }

    [Fact]
    public void Validator_rejects_a_due_date_that_contradicts_the_terms()
    {
        var result = _validator.Validate(Command(1, null, 30, "Net45"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Due date must be 45 days after the invoice date for Net45 terms.");
    }

    [Fact]
    public void Validator_counts_days_in_the_invoice_date_offset()
    {
        var command = Command(1, null, 0, "Net30") with { DueDate = InvoiceDate.AddDays(30).ToUniversalTime() };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Due_on_receipt_means_due_the_same_day()
    {
        _validator.Validate(Command(1, null, 0, "DueOnReceipt")).IsValid.Should().BeTrue();
        _validator.Validate(Command(1, null, 1, "DueOnReceipt")).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Typed_terms_are_stored()
    {
        var customer = await AddCustomerAsync();

        await _handler.Handle(Command(customer.Id, null, 15, "Net 15"), CancellationToken.None);

        _saved!.CreditTerms.Should().Be(CreditTerms.Net15);
    }

    [Fact]
    public async Task Order_terms_are_inherited_when_the_due_date_agrees()
    {
        var customer = await AddCustomerAsync(CreditTerms.Net60);
        var order = await AddOrderAsync(customer.Id, CreditTerms.Net45);

        await _handler.Handle(Command(customer.Id, order.Id, 45, null), CancellationToken.None);

        _saved!.CreditTerms.Should().Be(CreditTerms.Net45);
    }

    [Fact]
    public async Task Customer_default_terms_are_inherited_without_an_order()
    {
        var customer = await AddCustomerAsync(CreditTerms.Net60);

        await _handler.Handle(Command(customer.Id, null, 60, null), CancellationToken.None);

        _saved!.CreditTerms.Should().Be(CreditTerms.Net60);
    }

    [Fact]
    public async Task Inherited_terms_that_contradict_the_due_date_are_not_stamped()
    {
        var customer = await AddCustomerAsync();
        var order = await AddOrderAsync(customer.Id, CreditTerms.Net45);

        await _handler.Handle(Command(customer.Id, order.Id, 30, null), CancellationToken.None);

        _saved!.CreditTerms.Should().BeNull();
        _saved.DueDate.Should().Be(InvoiceDate.AddDays(30));
    }

    [Fact]
    public async Task Handler_refuses_unknown_terms_when_called_without_the_validator()
    {
        var customer = await AddCustomerAsync();

        var act = () => _handler.Handle(Command(customer.Id, null, 30, "Net 31"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
