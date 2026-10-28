using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;

using Forge.Api.Features.Invoices;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Capabilities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Invoices;

public class InvoiceTaxOverrideTests
{
    private static readonly DateTimeOffset InvoiceDate = new(2026, 3, 2, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IInvoiceRepository> _invoiceRepo = new();
    private readonly Mock<ICustomerRepository> _customerRepo = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private Invoice? _added;

    private async Task<Customer> SeedAsync(bool exempt = false)
    {
        var customer = new Customer { Name = "Taxable Co", IsTaxExempt = exempt };
        _db.Customers.Add(customer);
        _db.SalesTaxRates.Add(new SalesTaxRate
        {
            Name = "Default", Code = "DEF", Rate = 0.0725m, IsDefault = true, IsActive = true,
            EffectiveFrom = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });
        await _db.SaveChangesAsync();

        _customerRepo.Setup(r => r.FindAsync(customer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(customer);
        _invoiceRepo.Setup(r => r.GenerateNextInvoiceNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("INV-1");
        _invoiceRepo.Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Callback<Invoice, CancellationToken>((i, _) => { _added = i; _db.Invoices.Add(i); })
            .Returns(Task.CompletedTask);
        _invoiceRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => _db.SaveChangesAsync(ct));
        return customer;
    }

    private static CreateInvoiceCommand Command(
        int customerId, decimal taxRate, TaxOverrideReason? reason = null, string? note = null, bool enforce = true, int? salesOrderId = null)
        => new(customerId, salesOrderId, null, InvoiceDate, InvoiceDate.AddDays(30), null, taxRate, null,
            [new CreateInvoiceLineModel(null, "Goods", 1, 100m)],
            TaxOverrideReason: reason, TaxOverrideNote: note, EnforceTaxOverride: enforce);

    private Task Create(CreateInvoiceCommand command, string role = "OfficeManager")
    {
        var guard = new TaxOverrideGuard(_db, Mock.Of<IMediator>(), TestCaller.ClockAt(InvoiceDate), TestCaller.As(7, role));
        return new CreateInvoiceHandler(_invoiceRepo.Object, _customerRepo.Object, _db, taxGuard: guard)
            .Handle(command, CancellationToken.None);
    }

    [Fact]
    public async Task A_manual_zero_rate_on_a_taxable_customer_needs_a_reason()
    {
        var customer = await SeedAsync();

        var act = () => Create(Command(customer.Id, 0m));

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.ErrorCode == TaxOverrideGuard.ReasonRequiredCode);
        _added.Should().BeNull();
    }

    [Fact]
    public async Task A_reasoned_override_is_stamped_on_the_invoice_and_logged()
    {
        var customer = await SeedAsync();

        await Create(Command(customer.Id, 0m, TaxOverrideReason.OutOfJurisdiction, "Delivered out of state"));

        _added!.TaxRate.Should().Be(0m);
        _added.TaxOverrideReason.Should().Be(TaxOverrideReason.OutOfJurisdiction);
        _added.TaxOverrideNote.Should().Be("Delivered out of state");
        _db.AuditLogEntries.Should().Contain(a => a.Action == "invoice.tax_override" && a.EntityId == customer.Id);
        _db.ActivityLogs.Should().Contain(a => a.EntityType == "Invoice" && a.Action == "tax-override");
    }

    [Fact]
    public async Task The_computed_rate_needs_no_reason()
    {
        var customer = await SeedAsync();

        await Create(Command(customer.Id, 0.0725m));

        _added!.TaxOverrideReason.Should().BeNull();
    }

    [Fact]
    public async Task An_exempt_customer_is_unaffected()
    {
        var customer = await SeedAsync(exempt: true);

        await Create(Command(customer.Id, 0m), role: "Manager");

        _added!.TaxRate.Should().Be(0m);
        _added.TaxOverrideReason.Should().BeNull();
    }

    [Fact]
    public async Task Internal_invoices_that_do_not_enforce_the_override_are_unaffected()
    {
        var customer = await SeedAsync();

        await Create(Command(customer.Id, 0m, enforce: false));

        _added!.TaxRate.Should().Be(0m);
    }

    [Fact]
    public async Task An_invoice_at_its_sales_orders_rate_inherits_the_orders_override()
    {
        var customer = await SeedAsync();
        var order = new SalesOrder
        {
            OrderNumber = "SO-TX1", CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed, TaxRate = 0m,
            TaxOverrideReason = TaxOverrideReason.Resale, TaxOverrideNote = "Certificate R-77",
        };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();

        await Create(Command(customer.Id, 0m, salesOrderId: order.Id), role: "Manager");

        _added!.TaxOverrideReason.Should().Be(TaxOverrideReason.Resale);
        _added.TaxOverrideNote.Should().Be("Certificate R-77");
    }
}

[Collection(CapabilityTestCollection.Name)]
public class InvoiceTaxOverrideEndpointTests(CapabilityTestWebApplicationFactory factory)
{
    private HttpClient Client(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private async Task<int> SeedTaxableCustomerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rate = new SalesTaxRate
        {
            Name = "Override test county", Code = $"OVR-{Guid.NewGuid():N}"[..20], Rate = 0.065m, IsActive = true,
            EffectiveFrom = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        db.SalesTaxRates.Add(rate);
        await db.SaveChangesAsync();
        var customer = new Customer { Name = "Override test customer", DefaultTaxCodeId = rate.Id };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private static JsonContent Body(int customerId, string? reason = null, string? note = null) => JsonContent.Create(new
    {
        customerId,
        invoiceDate = DateTimeOffset.UtcNow,
        dueDate = DateTimeOffset.UtcNow.AddDays(30),
        taxRate = 0m,
        taxOverrideReason = reason,
        taxOverrideNote = note,
        lines = new[] { new { description = "Widget", quantity = 1m, unitPrice = 10m } },
    });

    [Fact]
    public async Task A_manual_zero_rate_without_a_reason_is_a_400_with_a_code()
    {
        var customerId = await SeedTaxableCustomerAsync();

        var response = await Client("OfficeManager").PostAsync("/api/v1/invoices", Body(customerId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(TaxOverrideGuard.ReasonRequiredCode);
    }

    [Fact]
    public async Task A_reasoned_override_is_created_and_shown_on_the_invoice()
    {
        var customerId = await SeedTaxableCustomerAsync();
        var client = Client("Manager");

        var response = await client.PostAsync("/api/v1/invoices", Body(customerId, "Other", "Sample shipment, no charge"));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/invoices/{id}");
        detail.GetProperty("taxOverrideReason").GetString().Should().Be("Other");
        detail.GetProperty("taxOverrideNote").GetString().Should().Be("Sample shipment, no charge");
    }

    [Fact]
    public async Task A_project_manager_overriding_a_sales_order_rate_is_a_403_with_a_code()
    {
        var customerId = await SeedTaxableCustomerAsync();

        var response = await Client("PM").PostAsync("/api/v1/orders", JsonContent.Create(new
        {
            customerId,
            taxRate = 0m,
            taxOverrideReason = "OutOfJurisdiction",
            lines = new[] { new { description = "Widget", quantity = 1m, unitPrice = 10m } },
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain(TaxOverrideGuard.ForbiddenCode);
    }
}
