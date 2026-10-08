using FluentAssertions;
using Forge.Api.Features.SalesOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

public class GetSalesOrdersListEntityTests
{
    private readonly AppDbContext _db;
    private readonly GetSalesOrdersListHandler _handler;
    private int _nextId = 500;

    public GetSalesOrdersListEntityTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new GetSalesOrdersListHandler(_db);
    }

    private async Task<Customer> SeedCustomerAsync(string name = "List Co")
    {
        var customer = new Customer { Name = name };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        return customer;
    }

    private async Task<SalesOrder> SeedOrderAsync(
        Customer customer, string orderNumber, SalesOrderStatus status, string? customerPO = null,
        params (decimal Qty, decimal Price)[] lines)
    {
        var so = new SalesOrder
        {
            Id = _nextId++,
            OrderNumber = orderNumber,
            CustomerId = customer.Id,
            Customer = customer,
            Status = status,
            CustomerPO = customerPO,
        };
        var lineNumber = 1;
        foreach (var (qty, price) in lines)
            so.Lines.Add(new SalesOrderLine { Description = $"Line {lineNumber}", Quantity = qty, UnitPrice = price, LineNumber = lineNumber++ });
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();
        return so;
    }

    private async Task SeedJobsForAsync(SalesOrder so, int count)
    {
        var stage = new JobStage { Id = 71, TrackTypeId = 7, Name = "Order Confirmed", Code = "order_confirmed" };
        if (!_db.JobStages.Any(s => s.Id == stage.Id)) _db.JobStages.Add(stage);
        var line = so.Lines.First();
        for (var i = 0; i < count; i++)
        {
            _db.Jobs.Add(new Job
            {
                Id = 40 + i,
                JobNumber = $"J-{40 + i}",
                Title = "Build",
                TrackTypeId = 7,
                CurrentStageId = stage.Id,
                CustomerId = so.CustomerId,
                SalesOrderLineId = line.Id,
                QuotedPrice = 0m,
            });
        }
        await _db.SaveChangesAsync();
    }

    private Task<PagedResponse<SalesOrderListItemModel>> ListAsync(SalesOrderListQuery query) =>
        _handler.Handle(new GetSalesOrdersListQuery(query), CancellationToken.None);

    [Fact]
    public async Task Confirmed_order_with_work_orders_is_one_row_with_its_lines_and_total()
    {
        var customer = await SeedCustomerAsync();
        var so = await SeedOrderAsync(customer, "SO-00605", SalesOrderStatus.Confirmed, "PO-9",
            (2m, 5m), (3m, 10m));
        await SeedJobsForAsync(so, 2);

        var result = await ListAsync(new SalesOrderListQuery());

        result.TotalCount.Should().Be(1);
        var row = result.Items.Should().ContainSingle().Subject;
        row.OrderNumber.Should().Be("SO-00605");
        row.Status.Should().Be("Confirmed");
        row.LineCount.Should().Be(2);
        row.Total.Should().Be(40m);
        row.CustomerPO.Should().Be("PO-9");
        row.Id.Should().Be(so.Id);
        row.SalesOrderId.Should().Be(so.Id);
        row.JobId.Should().BeNull();
        result.Items.Should().NotContain(i => i.OrderNumber.StartsWith("J-"));
    }

    [Fact]
    public async Task Every_status_is_listed_once()
    {
        var customer = await SeedCustomerAsync();
        foreach (var status in Enum.GetValues<SalesOrderStatus>())
            await SeedOrderAsync(customer, $"SO-{status}", status, null, (1m, 1m));

        var result = await ListAsync(new SalesOrderListQuery { PageSize = 50 });

        result.TotalCount.Should().Be(Enum.GetValues<SalesOrderStatus>().Length);
        result.Items.Select(i => i.Status).Should().BeEquivalentTo(
            Enum.GetNames<SalesOrderStatus>());
    }

    [Theory]
    [InlineData("Confirmed", "SO-Confirmed")]
    [InlineData("partiallyshipped", "SO-PartiallyShipped")]
    [InlineData("Cancelled", "SO-Cancelled")]
    [InlineData("Draft", "SO-Draft")]
    public async Task Status_filter_takes_SalesOrderStatus_names(string filter, string expected)
    {
        var customer = await SeedCustomerAsync();
        foreach (var status in Enum.GetValues<SalesOrderStatus>())
            await SeedOrderAsync(customer, $"SO-{status}", status, null, (1m, 1m));

        var result = await ListAsync(new SalesOrderListQuery { Status = filter });

        result.Items.Select(i => i.OrderNumber).Should().Equal(expected);
        result.TotalCount.Should().Be(1);
    }

    [Theory]
    [InlineData("NotAStatus")]
    [InlineData("1")]
    public async Task Unknown_status_filter_matches_nothing(string filter)
    {
        var customer = await SeedCustomerAsync();
        await SeedOrderAsync(customer, "SO-1", SalesOrderStatus.Confirmed, null, (1m, 1m));

        var result = await ListAsync(new SalesOrderListQuery { Status = filter });

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Theory]
    [InlineData("so-00700", "SO-00700")]
    [InlineData("acme", "SO-00701")]
    [InlineData("po-555", "SO-00702")]
    public async Task Search_covers_order_number_customer_and_customer_PO(string term, string expected)
    {
        var plain = await SeedCustomerAsync("Plain Co");
        var acme = await SeedCustomerAsync("Acme Industrial");
        await SeedOrderAsync(plain, "SO-00700", SalesOrderStatus.Confirmed, null, (1m, 1m));
        await SeedOrderAsync(acme, "SO-00701", SalesOrderStatus.Draft, null, (1m, 1m));
        await SeedOrderAsync(plain, "SO-00702", SalesOrderStatus.Shipped, "PO-555", (1m, 1m));

        var result = await ListAsync(new SalesOrderListQuery { Q = term });

        result.Items.Select(i => i.OrderNumber).Should().Equal(expected);
    }

    [Fact]
    public async Task Total_sort_orders_by_the_line_total()
    {
        var customer = await SeedCustomerAsync();
        await SeedOrderAsync(customer, "SO-MID", SalesOrderStatus.Confirmed, null, (2m, 10m));
        await SeedOrderAsync(customer, "SO-LOW", SalesOrderStatus.Draft, null, (1m, 1m));
        await SeedOrderAsync(customer, "SO-HIGH", SalesOrderStatus.Completed, null, (10m, 10m));

        var result = await ListAsync(new SalesOrderListQuery { Sort = "total", Order = "desc" });

        result.Items.Select(i => i.OrderNumber).Should().Equal("SO-HIGH", "SO-MID", "SO-LOW");
    }

    [Fact]
    public async Task ById_projection_takes_the_sales_order_id()
    {
        var customer = await SeedCustomerAsync();
        var so = await SeedOrderAsync(customer, "SO-00800", SalesOrderStatus.InProduction, null, (4m, 2.5m));
        await SeedJobsForAsync(so, 1);

        var row = await new GetSalesOrderProjectionByIdHandler(_db)
            .Handle(new GetSalesOrderProjectionByIdQuery(so.Id), CancellationToken.None);

        row.Should().NotBeNull();
        row!.OrderNumber.Should().Be("SO-00800");
        row.Status.Should().Be("InProduction");
        row.Total.Should().Be(10m);
        row.SalesOrderId.Should().Be(so.Id);
        row.JobId.Should().BeNull();
    }
}
