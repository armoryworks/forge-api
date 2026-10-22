using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.Invoices;
using Forge.Api.Features.SalesTax;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Invoices;

public class CreateInvoiceFromJobHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 14, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IMediator> _mediator = new();
    private readonly CreateInvoiceFromJobHandler _handler;
    private CreateInvoiceCommand? _sent;

    public CreateInvoiceFromJobHandlerTests()
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        _mediator
            .Setup(m => m.Send(It.IsAny<CreateInvoiceCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<InvoiceListItemModel>, CancellationToken>((cmd, _) => _sent = (CreateInvoiceCommand)cmd)
            .ReturnsAsync(new InvoiceListItemModel(9, "INV-00009", 1, "Acme", "Draft", Now, Now, 0m, 0m, 0m, Now));
        _handler = new CreateInvoiceFromJobHandler(_db, _mediator.Object, clock.Object);
    }

    private async Task<Customer> AddCustomerAsync()
    {
        var customer = new Customer { Name = "Acme" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        return customer;
    }

    private async Task<Job> AddJobAsync(int? customerId, Action<Job>? configure = null)
    {
        var job = new Job
        {
            JobNumber = "J-13",
            Title = "Mold T112",
            Description = "Tooling build",
            TrackTypeId = 1,
            CurrentStageId = 1,
            CustomerId = customerId,
            CompletedDate = Now.AddDays(-1),
        };
        configure?.Invoke(job);
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    private async Task<SalesOrderLine> AddOrderLineAsync(int customerId, int? partId, decimal quantity, decimal unitPrice)
    {
        var order = new SalesOrder { OrderNumber = "SO-0042", CustomerId = customerId, Status = SalesOrderStatus.Confirmed, TaxRate = 0.0725m };
        var line = new SalesOrderLine { PartId = partId, Description = "NRE tooling", Quantity = quantity, UnitPrice = unitPrice, LineNumber = 1 };
        order.Lines.Add(line);
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        return line;
    }

    [Fact]
    public async Task Job_linked_to_an_order_line_invoices_that_line_at_its_price_and_the_order_tax_rate()
    {
        var customer = await AddCustomerAsync();
        var part = new Part { PartNumber = "NRE-T112", Name = "Tooling NRE", InventoryClass = InventoryClass.Service };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        var line = await AddOrderLineAsync(customer.Id, part.Id, 1m, 6800m);
        var job = await AddJobAsync(customer.Id, j => { j.SalesOrderLineId = line.Id; j.PartId = part.Id; });

        var result = await _handler.Handle(new CreateInvoiceFromJobCommand(job.Id), CancellationToken.None);

        result.InvoiceNumber.Should().Be("INV-00009");
        _sent.Should().NotBeNull();
        _sent!.SalesOrderId.Should().Be(line.SalesOrderId);
        _sent.TaxRate.Should().Be(0.0725m);
        _sent.InvoiceDate.Should().Be(Now);
        _sent.DueDate.Should().Be(Now.AddDays(30));
        _sent.CreditTerms.Should().Be("Net30");
        _sent.Lines.Should().ContainSingle().Which.Should().Be(
            new CreateInvoiceLineModel(part.Id, "J-13 — Mold T112: Tooling build", 1m, 6800m, SalesOrderLineId: line.Id));
    }

    [Fact]
    public async Task Already_invoiced_quantity_is_left_off()
    {
        var customer = await AddCustomerAsync();
        var part = new Part { PartNumber = "FG-1", Name = "Widget" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        var line = await AddOrderLineAsync(customer.Id, part.Id, 10m, 25m);
        var invoice = new Invoice { InvoiceNumber = "INV-1", CustomerId = customer.Id, SalesOrderId = line.SalesOrderId };
        invoice.Lines.Add(new InvoiceLine { PartId = part.Id, Description = "Widget", Quantity = 4m, UnitPrice = 25m, LineNumber = 1 });
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();
        var job = await AddJobAsync(customer.Id, j => j.SalesOrderLineId = line.Id);

        await _handler.Handle(new CreateInvoiceFromJobCommand(job.Id), CancellationToken.None);

        _sent!.Lines.Single().Quantity.Should().Be(6m);
    }

    [Fact]
    public async Task Fully_invoiced_order_line_is_refused()
    {
        var customer = await AddCustomerAsync();
        var part = new Part { PartNumber = "FG-2", Name = "Widget" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        var line = await AddOrderLineAsync(customer.Id, part.Id, 2m, 25m);
        var invoice = new Invoice { InvoiceNumber = "INV-2", CustomerId = customer.Id, SalesOrderId = line.SalesOrderId };
        invoice.Lines.Add(new InvoiceLine { PartId = part.Id, Description = "Widget", Quantity = 2m, UnitPrice = 25m, LineNumber = 1 });
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();
        var job = await AddJobAsync(customer.Id, j => j.SalesOrderLineId = line.Id);

        var act = () => _handler.Handle(new CreateInvoiceFromJobCommand(job.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already invoiced in full*");
        _sent.Should().BeNull();
    }

    [Fact]
    public async Task Standalone_job_uses_its_quoted_price_and_the_customer_tax_rate()
    {
        var customer = await AddCustomerAsync();
        var job = await AddJobAsync(customer.Id, j => j.QuotedPrice = 1500m);
        _mediator
            .Setup(m => m.Send(It.IsAny<GetTaxRateForCustomerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesTaxRateResponseModel(5, "Utah County", "UT-UTCO", "UT", 0.0725m, Now, null, true, true, null, false, null));

        await _handler.Handle(new CreateInvoiceFromJobCommand(job.Id), CancellationToken.None);

        _sent!.SalesOrderId.Should().BeNull();
        _sent.CustomerId.Should().Be(customer.Id);
        _sent.TaxRate.Should().Be(0.0725m);
        _sent.Lines.Should().ContainSingle().Which.UnitPrice.Should().Be(1500m);
    }

    [Fact]
    public async Task Order_terms_set_the_credit_terms_and_the_due_date()
    {
        var customer = await AddCustomerAsync();
        customer.DefaultCreditTerms = CreditTerms.Net60;
        await _db.SaveChangesAsync();
        var line = await AddOrderLineAsync(customer.Id, null, 1m, 500m);
        var order = await _db.SalesOrders.FindAsync(line.SalesOrderId);
        order!.CreditTerms = CreditTerms.DueOnReceipt;
        await _db.SaveChangesAsync();
        var job = await AddJobAsync(customer.Id, j => j.SalesOrderLineId = line.Id);

        await _handler.Handle(new CreateInvoiceFromJobCommand(job.Id), CancellationToken.None);

        _sent!.CreditTerms.Should().Be("DueOnReceipt");
        _sent.DueDate.Should().Be(Now);
    }

    [Fact]
    public async Task Customer_default_terms_apply_when_the_order_has_none()
    {
        var customer = await AddCustomerAsync();
        customer.DefaultCreditTerms = CreditTerms.Net45;
        await _db.SaveChangesAsync();
        var job = await AddJobAsync(customer.Id, j => j.QuotedPrice = 1500m);

        await _handler.Handle(new CreateInvoiceFromJobCommand(job.Id), CancellationToken.None);

        _sent!.CreditTerms.Should().Be("Net45");
        _sent.DueDate.Should().Be(Now.AddDays(45));
    }

    [Fact]
    public async Task Job_with_no_price_is_refused_instead_of_drafted_at_zero()
    {
        var customer = await AddCustomerAsync();
        var job = await AddJobAsync(customer.Id);

        var act = () => _handler.Handle(new CreateInvoiceFromJobCommand(job.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*J-13 has no price*");
        _sent.Should().BeNull();
    }

    [Fact]
    public async Task Job_not_found_is_404()
    {
        var act = () => _handler.Handle(new CreateInvoiceFromJobCommand(999), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*999*not found*");
    }

    [Fact]
    public async Task Open_job_is_refused()
    {
        var customer = await AddCustomerAsync();
        var job = await AddJobAsync(customer.Id, j => { j.CompletedDate = null; j.QuotedPrice = 100m; });

        var act = () => _handler.Handle(new CreateInvoiceFromJobCommand(job.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not completed*");
    }

    [Fact]
    public async Task Job_without_customer_is_refused()
    {
        var job = await AddJobAsync(null, j => j.QuotedPrice = 100m);

        var act = () => _handler.Handle(new CreateInvoiceFromJobCommand(job.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*without a customer*");
    }

    [Fact]
    public async Task Voided_invoice_quantity_can_be_invoiced_again()
    {
        var customer = await AddCustomerAsync();
        var part = new Part { PartNumber = "FG-3", Name = "Widget" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        var line = await AddOrderLineAsync(customer.Id, part.Id, 2m, 25m);
        var invoice = new Invoice
        {
            InvoiceNumber = "INV-3", CustomerId = customer.Id, SalesOrderId = line.SalesOrderId, Status = InvoiceStatus.Voided,
        };
        invoice.Lines.Add(new InvoiceLine { PartId = part.Id, Description = "Widget", Quantity = 2m, UnitPrice = 25m, LineNumber = 1 });
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();
        var job = await AddJobAsync(customer.Id, j => j.SalesOrderLineId = line.Id);

        await _handler.Handle(new CreateInvoiceFromJobCommand(job.Id), CancellationToken.None);

        _sent!.Lines.Single().Quantity.Should().Be(2m);
    }
}
