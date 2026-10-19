using FluentAssertions;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Repositories;

public class SankeyReportRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddDays(-30);
    private static readonly DateTimeOffset End = DateTimeOffset.UtcNow.AddDays(1);

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    public void Dispose() => _db.Dispose();

    private Invoice AddInvoice(int id, int customerId, InvoiceStatus status, decimal taxRate, params (decimal Qty, decimal Price)[] lines)
    {
        var invoice = new Invoice
        {
            Id = id,
            InvoiceNumber = $"INV-{id}",
            CustomerId = customerId,
            Status = status,
            TaxRate = taxRate,
            InvoiceDate = DateTimeOffset.UtcNow.AddDays(-2),
            DueDate = DateTimeOffset.UtcNow.AddDays(28),
        };
        var lineNumber = 1;
        foreach (var (qty, price) in lines)
            invoice.Lines.Add(new InvoiceLine { LineNumber = lineNumber++, Description = "Part", Quantity = qty, UnitPrice = price });
        _db.Invoices.Add(invoice);
        return invoice;
    }

    [Fact]
    public async Task Customer_revenue_sums_invoice_lines_and_tax_including_overdue_but_not_draft()
    {
        _db.Customers.AddRange(new Customer { Id = 1, Name = "Alpha" }, new Customer { Id = 2, Name = "Beta" });
        AddInvoice(1, 1, InvoiceStatus.Sent, 0.0725m, (3, 10.05m), (1, 100m));
        AddInvoice(2, 1, InvoiceStatus.Overdue, 0m, (2, 50m));
        AddInvoice(3, 2, InvoiceStatus.Draft, 0m, (1, 999m));
        AddInvoice(4, 2, InvoiceStatus.Voided, 0m, (1, 999m));
        await _db.SaveChangesAsync();

        var flows = await new SankeyReportRepository(_db).GetCustomerRevenueFlowAsync(Start, End, CancellationToken.None);

        var flow = flows.Should().ContainSingle().Subject;
        flow.From.Should().Be("Alpha");
        flow.Flow.Should().Be(Math.Round(130.15m * 1.0725m, 2, MidpointRounding.AwayFromZero) + 100m);
        flow.Unit.Should().Be(SankeyFlowItem.CurrencyUnit);
    }

    [Fact]
    public async Task Customer_revenue_filters_on_invoice_date()
    {
        _db.Customers.Add(new Customer { Id = 1, Name = "Alpha" });
        var old = AddInvoice(1, 1, InvoiceStatus.Paid, 0m, (1, 100m));
        old.InvoiceDate = DateTimeOffset.UtcNow.AddDays(-90);
        await _db.SaveChangesAsync();

        var flows = await new SankeyReportRepository(_db).GetCustomerRevenueFlowAsync(Start, End, CancellationToken.None);

        flows.Should().BeEmpty();
    }

    [Fact]
    public async Task Vendor_supply_chain_is_weighted_by_ordered_value()
    {
        _db.Vendors.Add(new Vendor { Id = 1, CompanyName = "Resin Supply" });
        _db.Parts.AddRange(
            new Part { Id = 10, PartNumber = "INSERT", Name = "Insert" },
            new Part { Id = 20, PartNumber = "RESIN", Name = "Resin" });
        var po = new PurchaseOrder { Id = 1, PONumber = "PO-1", VendorId = 1 };
        po.Lines.Add(new PurchaseOrderLine { PartId = 10, Description = "Insert", OrderedQuantity = 10000, UnitPrice = 0.02m });
        po.Lines.Add(new PurchaseOrderLine { PartId = 20, Description = "Resin", OrderedQuantity = 25, UnitPrice = 12m });
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();

        var flows = await new SankeyReportRepository(_db).GetVendorSupplyChainFlowAsync(CancellationToken.None);

        flows.Should().BeEquivalentTo(
        [
            new SankeyFlowItem("Resin Supply", "RESIN", 300m, SankeyFlowItem.CurrencyUnit),
            new SankeyFlowItem("Resin Supply", "INSERT", 200m, SankeyFlowItem.CurrencyUnit),
        ], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Quote_to_cash_counts_sales_orders_after_the_order_node_so_it_balances()
    {
        _db.Customers.Add(new Customer { Id = 1, Name = "Alpha" });
        _db.Quotes.Add(new Quote { Id = 1, Type = QuoteType.Quote, CustomerId = 1, Status = QuoteStatus.ConvertedToOrder });
        _db.SalesOrders.AddRange(
            new SalesOrder { Id = 1, OrderNumber = "SO-1", CustomerId = 1, QuoteId = 1, Status = SalesOrderStatus.Confirmed },
            new SalesOrder { Id = 2, OrderNumber = "SO-2", CustomerId = 1, Status = SalesOrderStatus.Confirmed },
            new SalesOrder { Id = 3, OrderNumber = "SO-3", CustomerId = 1, Status = SalesOrderStatus.Confirmed });
        var first = AddInvoice(1, 1, InvoiceStatus.Paid, 0m, (1, 10m));
        first.SalesOrderId = 1;
        var second = AddInvoice(2, 1, InvoiceStatus.Sent, 0m, (1, 10m));
        second.SalesOrderId = 1;
        var third = AddInvoice(3, 1, InvoiceStatus.Paid, 0m, (1, 10m));
        third.SalesOrderId = 2;
        await _db.SaveChangesAsync();

        var flows = await new SankeyReportRepository(_db).GetQuoteToCashFlowAsync(Start, End, CancellationToken.None);

        decimal Flow(string from, string to) => flows.Where(f => f.From == from && f.To == to).Sum(f => f.Flow);
        Flow("Quotes", "Sales Orders").Should().Be(1);
        Flow("Direct Orders", "Sales Orders").Should().Be(2);
        Flow("Sales Orders", "Invoiced").Should().Be(2);
        Flow("Sales Orders", "Not Yet Invoiced").Should().Be(1);
        Flow("Invoiced", "Paid in Full").Should().Be(1);
        Flow("Invoiced", "Balance Outstanding").Should().Be(1);
        flows.Should().OnlyContain(f => f.Unit == SankeyFlowItem.CountUnit);
    }
}
