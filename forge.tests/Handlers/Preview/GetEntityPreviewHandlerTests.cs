using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;

using Forge.Api.Authorization;
using Forge.Api.Features.Preview;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Preview;

/// <summary>
/// Covers the entity-preview resolver: non-sensitive basics for a customer and
/// a sales-order, the unknown-type → null contract that the controller maps
/// to 404, and the same null for a record the caller's roles cannot open.
/// </summary>
public class GetEntityPreviewHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly GetEntityPreviewHandler _handler;

    public GetEntityPreviewHandlerTests()
    {
        _handler = HandlerFor("Admin");
    }

    [Fact]
    public async Task Customer_ReturnsIdentityAndStatus()
    {
        var customer = new Customer { Name = "Acme Corp", CustomerNumber = "CUST-1", IsActive = true };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new GetEntityPreviewQuery("customer", customer.Id), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Type.Should().Be("customer");
        result.Id.Should().Be(customer.Id);
        result.Title.Should().Be("Acme Corp");
        result.Subtitle.Should().Be("CUST-1");
        result.Fields.Should().ContainSingle(f => f.Label == "Status" && f.Value == "Active");
    }

    [Fact]
    public async Task SalesOrder_ReturnsNumberCustomerAndJumpLink()
    {
        var customer = new Customer { Name = "Acme Corp", CustomerNumber = "CUST-1", IsActive = true };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var order = new SalesOrder
        {
            OrderNumber = "SO-1055",
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Confirmed,
        };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new GetEntityPreviewQuery("sales-order", order.Id), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Title.Should().Be("SO-1055");
        result.Subtitle.Should().Be("Acme Corp");
        result.Fields.Should().Contain(f => f.Label == "Status" && f.Value == "Confirmed");
        result.Fields.Should().Contain(f => f.Key == "status" && f.Kind == "enum" && f.RawValue == "Confirmed");
        result.Fields.Should().Contain(f => f.Key == "total" && f.Kind == "money" && f.RawValue == "0" && f.Value == "$0.00");
        result.Links.Should().Contain(l => l.Type == "customer" && l.Id == customer.Id);
    }

    [Fact]
    public async Task Invoice_InForeignCurrency_FormatsTheTotalWithSymbolAndCode()
    {
        _db.Currencies.AddRange(
            new Currency { Code = "USD", Name = "US Dollar", Symbol = "$", DecimalPlaces = 2, IsBaseCurrency = true },
            new Currency { Code = "CAD", Name = "Canadian Dollar", Symbol = "$", DecimalPlaces = 2 });
        var customer = new Customer { Name = "Acme Corp", CustomerNumber = "CUST-1", IsActive = true };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        var invoice = new Invoice
        {
            InvoiceNumber = "INV-3001",
            CustomerId = customer.Id,
            CurrencyId = _db.Currencies.Single(c => c.Code == "CAD").Id,
            Status = InvoiceStatus.Sent,
            Lines = { new InvoiceLine { LineNumber = 1, Description = "Bracket", Quantity = 2m, UnitPrice = 617.25m } },
        };
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new GetEntityPreviewQuery("invoice", invoice.Id), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Fields.Should().ContainSingle(f => f.Key == "total").Which.Should().BeEquivalentTo(new
        {
            Kind = "money",
            Value = "$1,234.50 CAD",
            RawValue = "1234.50",
            Currency = "CAD",
        });
    }

    [Fact]
    public async Task UnknownType_ReturnsNull()
    {
        var result = await _handler.Handle(new GetEntityPreviewQuery("banana", 1), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SalesOrder_IsHidden_FromARoleThatCannotOpenSalesOrders()
    {
        var customer = new Customer { Name = "Acme Corp", CustomerNumber = "CUST-1", IsActive = true };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        var order = new SalesOrder { OrderNumber = "SO-2001", CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();

        var result = await HandlerFor("ProductionWorker")
            .Handle(new GetEntityPreviewQuery("sales-order", order.Id), CancellationToken.None);

        result.Should().BeNull("the preview carries the order total, which a shop-floor worker cannot open");
    }

    [Fact]
    public async Task Part_StillPreviews_ForAShopFloorWorker()
    {
        var part = new Part { PartNumber = "P-100", Name = "Bracket" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        var result = await HandlerFor("ProductionWorker")
            .Handle(new GetEntityPreviewQuery("part", part.Id), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Title.Should().Be("P-100");
    }

    private GetEntityPreviewHandler HandlerFor(string role)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "5"), new Claim(ClaimTypes.Role, role)], "Test"));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        return new GetEntityPreviewHandler(_db, new EntityAccessResolver(_db), accessor);
    }
}
