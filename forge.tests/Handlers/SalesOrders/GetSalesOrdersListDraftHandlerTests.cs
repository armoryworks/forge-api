using FluentAssertions;
using Forge.Api.Features.SalesOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

/// <summary>
/// #25 — the Sales Orders list (a Job projection) must also surface Draft entity-SOs,
/// otherwise a freshly-converted order is invisible. These cover the draft block only;
/// no Jobs are seeded (jobTotal = 0), so the list is driven by the draft surface.
/// </summary>
public class GetSalesOrdersListDraftHandlerTests
{
    private readonly AppDbContext _db;
    private readonly GetSalesOrdersListHandler _handler;

    public GetSalesOrdersListDraftHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new GetSalesOrdersListHandler(_db);
    }

    private async Task<SalesOrder> SeedDraftAsync(string orderNumber = "SO-DRAFT-1")
    {
        var customer = new Customer { Name = "Draft Co" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var so = new SalesOrder
        {
            OrderNumber = orderNumber,
            CustomerId = customer.Id,
            Customer = customer,
            Status = SalesOrderStatus.Draft,
            CustomerPO = "PO-77",
            Lines = { new SalesOrderLine { Description = "Widget", Quantity = 3m, UnitPrice = 4m, LineNumber = 1 } },
        };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();
        return so;
    }

    [Fact] // #25 — a Draft SO shows up in the default (unfiltered) list, with its real fields.
    public async Task Draft_sales_order_is_surfaced_in_the_list()
    {
        var so = await SeedDraftAsync();

        var result = await _handler.Handle(
            new GetSalesOrdersListQuery(new SalesOrderListQuery()), CancellationToken.None);

        result.TotalCount.Should().BeGreaterThanOrEqualTo(1);
        var row = result.Items.Single(i => i.OrderNumber == so.OrderNumber);
        row.Status.Should().Be("Draft");
        row.Id.Should().Be(so.Id, "the draft row carries the real SalesOrder id so it opens at /orders/{id}");
        row.CustomerPO.Should().Be("PO-77");
        row.LineCount.Should().Be(1);
        row.Total.Should().Be(12m, "3 × 4");
        result.Items.First().OrderNumber.Should().Be(so.OrderNumber, "drafts lead the list (pending orders first)");
    }

    [Fact] // #25 — Status=Draft filters to only draft entity-SOs.
    public async Task Status_filter_Draft_returns_drafts()
    {
        var so = await SeedDraftAsync("SO-DRAFT-2");

        var result = await _handler.Handle(
            new GetSalesOrdersListQuery(new SalesOrderListQuery { Status = "Draft" }), CancellationToken.None);

        result.Items.Should().OnlyContain(i => i.Status == "Draft");
        result.Items.Select(i => i.OrderNumber).Should().Contain(so.OrderNumber);
    }

    [Fact] // #25 — a production status filter must NOT include drafts.
    public async Task Production_status_filter_excludes_drafts()
    {
        await SeedDraftAsync("SO-DRAFT-3");

        var result = await _handler.Handle(
            new GetSalesOrdersListQuery(new SalesOrderListQuery { Status = "Confirmed" }), CancellationToken.None);

        result.Items.Should().NotContain(i => i.Status == "Draft");
    }
}
