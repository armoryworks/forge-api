using FluentAssertions;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Features.Accounting;

/// <summary>
/// Aggregation tests for the sales-tax liability report. Prove the report is built
/// from finalized invoices (not orders), groups by ship-to jurisdiction, excludes
/// marketplace-collected tax, leaves non-taxable lines out, converts to functional
/// currency and honors the invoice-date filter.
/// </summary>
public class GetSalesTaxLiabilityHandlerTests : IDisposable
{
    private static readonly DateTimeOffset October = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db;
    private readonly GetSalesTaxLiabilityHandler _handler;
    private int _customerId;

    public GetSalesTaxLiabilityHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new GetSalesTaxLiabilityHandler(_db);
    }

    private async Task SeedCustomerAsync(string? shippingState = null)
    {
        var customer = new Customer { Name = "Tax Test Customer" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        _customerId = customer.Id;
        if (shippingState is not null)
        {
            _db.CustomerAddresses.Add(new CustomerAddress
            {
                CustomerId = customer.Id, Label = "Dock", AddressType = AddressType.Shipping, Line1 = "1 Dock Rd",
                City = "Provo", State = shippingState, PostalCode = "84601", IsDefault = true,
            });
            await _db.SaveChangesAsync();
        }
    }

    private async Task<SalesOrder> AddOrderAsync(string state, TaxCollectedBy collectedBy = TaxCollectedBy.Seller)
    {
        var order = new SalesOrder
        {
            OrderNumber = $"SO-{Guid.NewGuid():N}",
            CustomerId = _customerId,
            Status = SalesOrderStatus.Confirmed,
            TaxRate = 0.10m,
            TaxCollectedBy = collectedBy,
            ShipTo = new OrderShipTo { Name = "Consumer", Line1 = "1 Main St", City = "Town", State = state, PostalCode = "00000" },
            Lines = [new SalesOrderLine { Description = "Widget", Quantity = 1m, UnitPrice = 999m, LineNumber = 1 }],
        };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    private async Task<Invoice> AddInvoiceAsync(
        SalesOrder? order,
        decimal taxRate,
        decimal taxableTotal,
        decimal nonTaxableTotal = 0m,
        InvoiceStatus status = InvoiceStatus.Sent,
        DateTimeOffset? invoiceDate = null,
        decimal fxRate = 1m)
    {
        var invoice = new Invoice
        {
            InvoiceNumber = $"INV-{Guid.NewGuid():N}"[..20],
            CustomerId = _customerId,
            SalesOrderId = order?.Id,
            Status = status,
            TaxRate = taxRate,
            FxRate = fxRate,
            InvoiceDate = invoiceDate ?? October,
            DueDate = invoiceDate ?? October,
        };
        invoice.Lines.Add(new InvoiceLine { Description = "Widget", Quantity = 1m, UnitPrice = taxableTotal, LineNumber = 1 });
        if (nonTaxableTotal > 0m)
            invoice.Lines.Add(new InvoiceLine
            {
                Description = "Freight", Quantity = 1m, UnitPrice = nonTaxableTotal, LineNumber = 2, TaxCode = InvoiceLine.NonTaxableCode,
            });
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public async Task GroupsByJurisdiction_AndSumsInvoicedTaxPerState()
    {
        await SeedCustomerAsync();
        var ca = await AddOrderAsync("CA");
        var tx = await AddOrderAsync("TX");
        await AddInvoiceAsync(ca, 0.10m, 100m);
        await AddInvoiceAsync(ca, 0.10m, 200m);
        await AddInvoiceAsync(tx, 0.05m, 100m);

        var report = await _handler.Handle(new GetSalesTaxLiabilityQuery(), default);

        report.Jurisdictions.Should().HaveCount(2);
        report.Jurisdictions[0].Jurisdiction.Should().Be("CA");
        report.Jurisdictions[0].Liability.Should().Be(30m);
        report.Jurisdictions[0].TaxableBase.Should().Be(300m);
        report.Jurisdictions[0].InvoiceCount.Should().Be(2);
        report.Jurisdictions.Single(j => j.Jurisdiction == "TX").Liability.Should().Be(5m);
        report.TotalLiability.Should().Be(35m);
        report.TotalTaxableBase.Should().Be(400m);
        report.TotalInvoiceCount.Should().Be(3);
    }

    [Fact]
    public async Task ConfirmedOrderWithoutAnInvoice_OwesNothingYet()
    {
        await SeedCustomerAsync();
        await AddOrderAsync("CA");

        var report = await _handler.Handle(new GetSalesTaxLiabilityQuery(), default);

        report.Jurisdictions.Should().BeEmpty();
        report.TotalLiability.Should().Be(0m);
    }

    [Fact]
    public async Task InvoiceWithoutAnOrder_IsCountedUnderTheCustomerShippingState()
    {
        await SeedCustomerAsync(shippingState: "ut");
        await AddInvoiceAsync(null, 0.0725m, 1000m);

        var report = await _handler.Handle(new GetSalesTaxLiabilityQuery(), default);

        report.Jurisdictions.Should().ContainSingle().Which.Jurisdiction.Should().Be("UT");
        report.TotalLiability.Should().Be(72.5m);
    }

    [Fact]
    public async Task MarketplaceCollectedInvoice_IsCountedButOwesNothing()
    {
        await SeedCustomerAsync();
        await AddInvoiceAsync(await AddOrderAsync("CA", TaxCollectedBy.Marketplace), 0.10m, 100m);
        await AddInvoiceAsync(await AddOrderAsync("CA"), 0.10m, 100m);

        var report = await _handler.Handle(new GetSalesTaxLiabilityQuery(), default);

        var ca = report.Jurisdictions.Single(j => j.Jurisdiction == "CA");
        ca.Liability.Should().Be(10m);
        ca.TaxableBase.Should().Be(100m);
        ca.InvoiceCount.Should().Be(2);
    }

    [Fact]
    public async Task NonTaxableLines_AreLeftOutOfBaseAndLiability()
    {
        await SeedCustomerAsync();
        await AddInvoiceAsync(await AddOrderAsync("UT"), 0.0725m, 400m, nonTaxableTotal: 40m);

        var report = await _handler.Handle(new GetSalesTaxLiabilityQuery(), default);

        report.TotalTaxableBase.Should().Be(400m);
        report.TotalLiability.Should().Be(29m);
    }

    [Fact]
    public async Task ForeignCurrencyInvoice_IsReportedInFunctionalCurrency()
    {
        await SeedCustomerAsync();
        await AddInvoiceAsync(await AddOrderAsync("WA"), 0.10m, 100m, fxRate: 0.75m);

        var report = await _handler.Handle(new GetSalesTaxLiabilityQuery(), default);

        report.TotalTaxableBase.Should().Be(75m);
        report.TotalLiability.Should().Be(7.5m);
    }

    [Fact]
    public async Task ForeignCurrencyInvoice_IsRoundedPerInvoiceLikeThePostedFunctionalAmount()
    {
        await SeedCustomerAsync();
        await AddInvoiceAsync(await AddOrderAsync("WA"), 0.0725m, 33.33m, fxRate: 0.73m);
        await AddInvoiceAsync(await AddOrderAsync("WA"), 0.0725m, 33.33m, fxRate: 0.73m);

        var report = await _handler.Handle(new GetSalesTaxLiabilityQuery(), default);

        report.TotalTaxableBase.Should().Be(48.66m);
        report.TotalLiability.Should().Be(3.54m);
    }

    [Fact]
    public async Task FractionalLine_ReportsTheRoundedLineAndTheInvoiceTax()
    {
        await SeedCustomerAsync();
        var invoice = await AddInvoiceAsync(await AddOrderAsync("WA"), 0.0725m, 33.333m);

        var report = await _handler.Handle(new GetSalesTaxLiabilityQuery(), default);

        report.TotalTaxableBase.Should().Be(33.33m);
        report.TotalLiability.Should().Be(2.42m);
        report.TotalLiability.Should().Be(invoice.TaxAmount);
    }

    [Fact]
    public async Task HalfCentLine_RoundsAwayFromZeroLikeTheInvoiceAndThePosting()
    {
        await SeedCustomerAsync();
        var invoice = await AddInvoiceAsync(await AddOrderAsync("WA"), 0.5m, 2.125m);

        var report = await _handler.Handle(new GetSalesTaxLiabilityQuery(), default);

        report.TotalTaxableBase.Should().Be(2.13m);
        report.TotalLiability.Should().Be(1.07m);
        report.TotalLiability.Should().Be(InvoiceArPostingService.ComputePostedAmounts(invoice).TaxAmount);
    }

    [Fact]
    public async Task DateFilter_UsesTheInvoiceDate()
    {
        await SeedCustomerAsync();
        var order = await AddOrderAsync("CA");
        await AddInvoiceAsync(order, 0.10m, 100m, invoiceDate: new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));
        await AddInvoiceAsync(order, 0.10m, 500m, invoiceDate: new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));

        var report = await _handler.Handle(
            new GetSalesTaxLiabilityQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)), default);

        report.Jurisdictions.Should().ContainSingle();
        report.TotalLiability.Should().Be(10m);
    }

    [Fact]
    public async Task DraftAndVoidedInvoices_AreExcluded()
    {
        await SeedCustomerAsync();
        var order = await AddOrderAsync("CA");
        await AddInvoiceAsync(order, 0.10m, 100m, status: InvoiceStatus.Draft);
        await AddInvoiceAsync(order, 0.10m, 100m, status: InvoiceStatus.Voided);
        await AddInvoiceAsync(order, 0.10m, 100m, status: InvoiceStatus.Paid);

        var report = await _handler.Handle(new GetSalesTaxLiabilityQuery(), default);

        report.Jurisdictions.Should().ContainSingle().Which.InvoiceCount.Should().Be(1);
        report.TotalLiability.Should().Be(10m);
    }

    [Fact]
    public async Task MissingShipToState_IsGroupedIntoTheUnassignedRow()
    {
        await SeedCustomerAsync();
        await AddInvoiceAsync(await AddOrderAsync("  "), 0.10m, 100m);

        var report = await _handler.Handle(new GetSalesTaxLiabilityQuery(), default);

        report.Jurisdictions.Should().ContainSingle().Which.Jurisdiction.Should().BeNull();
        report.TotalLiability.Should().Be(10m);
    }

    public void Dispose() => _db.Dispose();
}
