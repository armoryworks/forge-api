using FluentAssertions;

using Forge.Api.Features.Reports;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Reports;

public class GetJobMarginReportHandlerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 10, 31, 23, 59, 59, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task<SalesOrder> SeedOrderAsync(params SalesOrderLine[] lines)
    {
        var customer = new Customer { Id = 1, Name = "Margin Co" };
        _db.Customers.Add(customer);
        _db.Parts.AddRange(
            new Part { Id = 10, PartNumber = "P-10", Name = "Bracket" },
            new Part { Id = 20, PartNumber = "P-20", Name = "Housing" });
        var so = new SalesOrder { Id = 100, OrderNumber = "SO-100", CustomerId = customer.Id };
        foreach (var line in lines) so.Lines.Add(line);
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();
        return so;
    }

    private void AddJob(int id, int salesOrderLineId) =>
        _db.Jobs.Add(new Job
        {
            Id = id,
            JobNumber = $"J-{id}",
            Title = $"Job {id}",
            TrackTypeId = 1,
            CurrentStageId = 1,
            CustomerId = 1,
            SalesOrderLineId = salesOrderLineId,
            CreatedAt = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero),
        });

    private void AddInvoice(int id, InvoiceStatus status, params InvoiceLine[] lines)
    {
        var invoice = new Invoice
        {
            Id = id,
            InvoiceNumber = $"INV-{id}",
            CustomerId = 1,
            SalesOrderId = 100,
            Status = status,
            InvoiceDate = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero),
        };
        foreach (var line in lines) invoice.Lines.Add(line);
        _db.Invoices.Add(invoice);
    }

    private async Task<Dictionary<string, decimal>> RevenueByJobAsync()
    {
        var handler = new GetJobMarginReportHandler(_db);
        var rows = await handler.Handle(new GetJobMarginReportQuery(Start, End), CancellationToken.None);
        return rows.ToDictionary(r => r.JobNumber, r => r.Revenue);
    }

    [Fact]
    public async Task Each_job_earns_only_the_invoiced_lines_for_its_own_part()
    {
        await SeedOrderAsync(
            new SalesOrderLine { Id = 1, PartId = 10, Description = "Bracket", Quantity = 10, UnitPrice = 5, LineNumber = 1 },
            new SalesOrderLine { Id = 2, PartId = 20, Description = "Housing", Quantity = 2, UnitPrice = 100, LineNumber = 2 });
        AddJob(1, 1);
        AddJob(2, 2);
        AddInvoice(1, InvoiceStatus.Sent,
            new InvoiceLine { PartId = 10, Description = "Bracket", Quantity = 10, UnitPrice = 5, LineNumber = 1 },
            new InvoiceLine { PartId = 20, Description = "Housing", Quantity = 2, UnitPrice = 100, LineNumber = 2 });
        await _db.SaveChangesAsync();

        var revenue = await RevenueByJobAsync();

        revenue["J-1"].Should().Be(50m);
        revenue["J-2"].Should().Be(200m);
    }

    [Fact]
    public async Task Voided_and_draft_invoices_are_not_revenue()
    {
        await SeedOrderAsync(
            new SalesOrderLine { Id = 1, PartId = 10, Description = "Bracket", Quantity = 10, UnitPrice = 5, LineNumber = 1 });
        AddJob(1, 1);
        AddInvoice(1, InvoiceStatus.Paid,
            new InvoiceLine { PartId = 10, Description = "Bracket", Quantity = 4, UnitPrice = 5, LineNumber = 1 });
        AddInvoice(2, InvoiceStatus.Voided,
            new InvoiceLine { PartId = 10, Description = "Bracket", Quantity = 6, UnitPrice = 5, LineNumber = 1 });
        AddInvoice(3, InvoiceStatus.Draft,
            new InvoiceLine { PartId = 10, Description = "Bracket", Quantity = 6, UnitPrice = 5, LineNumber = 1 });
        await _db.SaveChangesAsync();

        var revenue = await RevenueByJobAsync();

        revenue["J-1"].Should().Be(20m);
    }

    [Fact]
    public async Task A_free_text_order_line_matches_invoice_lines_by_description()
    {
        await SeedOrderAsync(
            new SalesOrderLine { Id = 1, Description = "Custom fixture", Quantity = 1, UnitPrice = 900, LineNumber = 1 });
        AddJob(1, 1);
        AddInvoice(1, InvoiceStatus.Sent,
            new InvoiceLine { Description = "custom fixture ", Quantity = 1, UnitPrice = 900, LineNumber = 1 },
            new InvoiceLine { Description = "Freight", Quantity = 1, UnitPrice = 40, LineNumber = 2 });
        await _db.SaveChangesAsync();

        var revenue = await RevenueByJobAsync();

        revenue["J-1"].Should().Be(900m);
    }

    [Fact]
    public async Task Two_order_lines_for_the_same_part_split_one_untagged_invoice_line_instead_of_both_counting_it()
    {
        await SeedOrderAsync(
            new SalesOrderLine { Id = 1, PartId = 10, Description = "Bracket", Quantity = 10, UnitPrice = 5, LineNumber = 1 },
            new SalesOrderLine { Id = 2, PartId = 10, Description = "Bracket", Quantity = 10, UnitPrice = 5, LineNumber = 2 });
        AddJob(1, 1);
        AddJob(2, 2);
        AddInvoice(1, InvoiceStatus.Sent,
            new InvoiceLine { PartId = 10, Description = "Bracket", Quantity = 20, UnitPrice = 5, LineNumber = 1 });
        await _db.SaveChangesAsync();

        var revenue = await RevenueByJobAsync();

        revenue["J-1"].Should().Be(50m);
        revenue["J-2"].Should().Be(50m);
        revenue.Values.Sum().Should().Be(100m);
    }

    [Fact]
    public async Task Two_order_lines_for_the_same_part_each_earn_only_the_invoice_lines_that_name_them()
    {
        await SeedOrderAsync(
            new SalesOrderLine { Id = 1, PartId = 10, Description = "Bracket", Quantity = 10, UnitPrice = 5, LineNumber = 1 },
            new SalesOrderLine { Id = 2, PartId = 10, Description = "Bracket, anodized", Quantity = 10, UnitPrice = 8, LineNumber = 2 });
        AddJob(1, 1);
        AddJob(2, 2);
        AddInvoice(1, InvoiceStatus.Sent,
            new InvoiceLine { PartId = 10, SalesOrderLineId = 2, Description = "Bracket, anodized", Quantity = 10, UnitPrice = 8, LineNumber = 1 });
        AddInvoice(2, InvoiceStatus.Paid,
            new InvoiceLine { PartId = 10, SalesOrderLineId = 1, Description = "Bracket", Quantity = 4, UnitPrice = 5, LineNumber = 1 });
        await _db.SaveChangesAsync();

        var revenue = await RevenueByJobAsync();

        revenue["J-1"].Should().Be(20m);
        revenue["J-2"].Should().Be(80m);
    }

    [Fact]
    public async Task Revenue_from_a_foreign_currency_invoice_is_converted_at_its_fx_rate()
    {
        await SeedOrderAsync(
            new SalesOrderLine { Id = 1, PartId = 10, Description = "Bracket", Quantity = 10, UnitPrice = 5, LineNumber = 1 });
        AddJob(1, 1);
        AddInvoice(1, InvoiceStatus.Sent,
            new InvoiceLine { PartId = 10, SalesOrderLineId = 1, Description = "Bracket", Quantity = 10, UnitPrice = 5, LineNumber = 1 });
        _db.Invoices.Local.Single(i => i.Id == 1).FxRate = 0.73m;
        await _db.SaveChangesAsync();

        var revenue = await RevenueByJobAsync();

        revenue["J-1"].Should().Be(36.50m);
    }

    [Fact]
    public async Task A_job_with_revenue_but_no_recorded_cost_has_no_margin_instead_of_100_percent()
    {
        await SeedOrderAsync(
            new SalesOrderLine { Id = 1, PartId = 10, Description = "Bracket", Quantity = 10, UnitPrice = 5, LineNumber = 1 },
            new SalesOrderLine { Id = 2, PartId = 20, Description = "Housing", Quantity = 2, UnitPrice = 100, LineNumber = 2 });
        AddJob(1, 1);
        AddJob(2, 2);
        AddInvoice(1, InvoiceStatus.Sent,
            new InvoiceLine { PartId = 10, Description = "Bracket", Quantity = 10, UnitPrice = 5, LineNumber = 1 },
            new InvoiceLine { PartId = 20, Description = "Housing", Quantity = 2, UnitPrice = 100, LineNumber = 2 });
        _db.Expenses.Add(new Expense { JobId = 2, UserId = 1, Amount = 150m, Category = "Material", Description = "Castings" });
        await _db.SaveChangesAsync();

        var rows = (await new GetJobMarginReportHandler(_db).Handle(new GetJobMarginReportQuery(Start, End), CancellationToken.None))
            .ToDictionary(r => r.JobNumber);

        rows["J-1"].CostRecorded.Should().BeFalse();
        rows["J-1"].Margin.Should().BeNull();
        rows["J-1"].MarginPercentage.Should().BeNull();
        rows["J-2"].CostRecorded.Should().BeTrue();
        rows["J-2"].Margin.Should().Be(50m);
        rows["J-2"].MarginPercentage.Should().Be(25.0m);
    }
}
