using FluentAssertions;

using Forge.Api.Features.SalesOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

/// <summary>
/// F8 [dev-review]: once an order leaves Draft it is locked; changes ride on a
/// new linked Draft addendum ({parent}-A{n}, delta lines only) instead of
/// mutating the locked record.
/// </summary>
public class CreateAddendumOrderHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task<SalesOrder> SeedParentAsync(SalesOrderStatus status = SalesOrderStatus.Confirmed)
    {
        var customer = new Customer { Id = 1, Name = "Addendum Co" };
        _db.Customers.Add(customer);
        var parent = new SalesOrder
        {
            Id = 100, OrderNumber = "SO-00042", CustomerId = 1, Customer = customer,
            Status = status, TaxRate = 0.05m, CustomerPO = "PO-1", CreditTerms = CreditTerms.Net30,
        };
        _db.SalesOrders.Add(parent);
        await _db.SaveChangesAsync();
        return parent;
    }

    [Fact]
    public async Task Creates_linked_draft_with_suffix_numbering_and_copied_header()
    {
        var parent = await SeedParentAsync();

        var result = await new CreateAddendumOrderHandler(_db)
            .Handle(new CreateAddendumOrderCommand(parent.Id), CancellationToken.None);

        result.OrderNumber.Should().Be("SO-00042-A1");
        result.Status.Should().Be("Draft");
        result.CustomerPO.Should().Be("PO-1");

        var addendum = _db.SalesOrders.Single(o => o.Id == result.Id);
        addendum.ParentSalesOrderId.Should().Be(parent.Id);
        addendum.AddendumNumber.Should().Be(1);
        addendum.TaxRate.Should().Be(0.05m);
        addendum.CreditTerms.Should().Be(CreditTerms.Net30);
        addendum.Lines.Should().BeEmpty("an addendum carries only the delta");
    }

    [Fact]
    public async Task Second_addendum_increments_the_sequence()
    {
        var parent = await SeedParentAsync();
        var handler = new CreateAddendumOrderHandler(_db);
        await handler.Handle(new CreateAddendumOrderCommand(parent.Id), CancellationToken.None);

        var second = await handler.Handle(new CreateAddendumOrderCommand(parent.Id), CancellationToken.None);

        second.OrderNumber.Should().Be("SO-00042-A2");
    }

    [Theory]
    [InlineData(SalesOrderStatus.Draft, "still editable")]
    [InlineData(SalesOrderStatus.Cancelled, "cancelled")]
    public async Task Rejects_draft_and_cancelled_parents(SalesOrderStatus status, string reason)
    {
        var parent = await SeedParentAsync(status);

        var act = () => new CreateAddendumOrderHandler(_db)
            .Handle(new CreateAddendumOrderCommand(parent.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.ToLowerInvariant().Should().Contain(reason.Split(' ')[0]);
    }

    [Fact]
    public async Task Rejects_addendum_of_an_addendum()
    {
        var parent = await SeedParentAsync();
        var handler = new CreateAddendumOrderHandler(_db);
        var first = await handler.Handle(new CreateAddendumOrderCommand(parent.Id), CancellationToken.None);

        // Confirm the addendum so it passes the status gate, then try to chain.
        var addendum = _db.SalesOrders.Single(o => o.Id == first.Id);
        addendum.Status = SalesOrderStatus.Confirmed;
        await _db.SaveChangesAsync();

        var act = () => handler.Handle(new CreateAddendumOrderCommand(first.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*addendum of an addendum*");
    }
}
