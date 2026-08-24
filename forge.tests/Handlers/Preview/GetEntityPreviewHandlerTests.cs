using FluentAssertions;

using Forge.Api.Features.Preview;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Preview;

/// <summary>
/// Covers the entity-preview resolver: non-sensitive basics for a customer and
/// a sales-order, plus the unknown-type → null contract that the controller maps
/// to 404.
/// </summary>
public class GetEntityPreviewHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly GetEntityPreviewHandler _handler;

    public GetEntityPreviewHandlerTests()
    {
        _handler = new GetEntityPreviewHandler(_db);
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
        result.Links.Should().Contain(l => l.Type == "customer" && l.Id == customer.Id);
    }

    [Fact]
    public async Task UnknownType_ReturnsNull()
    {
        var result = await _handler.Handle(new GetEntityPreviewQuery("banana", 1), CancellationToken.None);

        result.Should().BeNull();
    }
}
