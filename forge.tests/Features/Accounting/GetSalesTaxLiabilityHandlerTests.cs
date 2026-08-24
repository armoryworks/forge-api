using FluentAssertions;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Features.Accounting;

/// <summary>
/// Aggregation tests for the sales-tax liability report. Prove the report groups
/// by ship-to jurisdiction, sums <c>SellerTaxLiability</c> (NOT <c>TaxAmount</c>
/// — marketplace-collected tax is excluded), and honors the date filter.
/// </summary>
public class GetSalesTaxLiabilityHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GetSalesTaxLiabilityHandler _handler;
    private int _customerId;

    public GetSalesTaxLiabilityHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new GetSalesTaxLiabilityHandler(_db);
    }

    private async Task SeedCustomerAsync()
    {
        var customer = new Customer { Name = "Tax Test Customer" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        _customerId = customer.Id;
    }

    private async Task<SalesOrder> AddOrderAsync(
        string state,
        decimal taxRate,
        decimal lineTotal,
        TaxCollectedBy collectedBy = TaxCollectedBy.Seller,
        SalesOrderStatus status = SalesOrderStatus.Confirmed,
        DateTimeOffset? confirmedDate = null)
    {
        var order = new SalesOrder
        {
            OrderNumber = $"SO-{Guid.NewGuid():N}",
            CustomerId = _customerId,
            Status = status,
            TaxRate = taxRate,
            TaxCollectedBy = collectedBy,
            ConfirmedDate = confirmedDate,
            ShipTo = new OrderShipTo
            {
                Name = "Consumer",
                Line1 = "1 Main St",
                City = "Town",
                State = state,
                PostalCode = "00000",
            },
            Lines =
            [
                new SalesOrderLine
                {
                    Description = "Widget",
                    Quantity = 1m,
                    UnitPrice = lineTotal,
                    LineNumber = 1,
                },
            ],
        };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    [Fact]
    public async Task GroupsByJurisdiction_AndSumsLiabilityPerState()
    {
        await SeedCustomerAsync();
        await AddOrderAsync("CA", 0.10m, 100m); // CA liability 10
        await AddOrderAsync("CA", 0.10m, 200m); // CA liability 20
        await AddOrderAsync("TX", 0.05m, 100m); // TX liability 5

        var report = await _handler.Handle(new GetSalesTaxLiabilityQuery(), default);

        report.Jurisdictions.Should().HaveCount(2);
        report.Jurisdictions[0].Jurisdiction.Should().Be("CA");
        report.Jurisdictions[0].Liability.Should().Be(30m);
        report.Jurisdictions[0].TaxableBase.Should().Be(300m);
        report.Jurisdictions[0].OrderCount.Should().Be(2);

        var tx = report.Jurisdictions.Single(j => j.Jurisdiction == "TX");
        tx.Liability.Should().Be(5m);

        report.TotalLiability.Should().Be(35m);
        report.TotalTaxableBase.Should().Be(400m);
        report.TotalOrderCount.Should().Be(3);
    }

    [Fact]
    public async Task UsesSellerTaxLiability_NotTaxAmount_ForMarketplaceOrders()
    {
        await SeedCustomerAsync();
        // A marketplace-collected order: TaxAmount = 100 * 0.10 = 10, but the
        // platform remits it, so SellerTaxLiability = 0. The report must reflect 0.
        await AddOrderAsync("CA", 0.10m, 100m, TaxCollectedBy.Marketplace);
        // A seller-collected order in the same state contributes its full liability.
        await AddOrderAsync("CA", 0.10m, 100m, TaxCollectedBy.Seller);

        var report = await _handler.Handle(new GetSalesTaxLiabilityQuery(), default);

        var ca = report.Jurisdictions.Single(j => j.Jurisdiction == "CA");
        // If the report summed TaxAmount this would be 20; SellerTaxLiability → 10.
        ca.Liability.Should().Be(10m);
        // Taxable base excludes the marketplace order's subtotal too.
        ca.TaxableBase.Should().Be(100m);
        ca.OrderCount.Should().Be(2);
        report.TotalLiability.Should().Be(10m);
    }

    [Fact]
    public async Task DateFilter_ExcludesOrdersOutsideWindow()
    {
        await SeedCustomerAsync();
        await AddOrderAsync("CA", 0.10m, 100m, confirmedDate: new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));
        await AddOrderAsync("CA", 0.10m, 500m, confirmedDate: new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));

        var report = await _handler.Handle(
            new GetSalesTaxLiabilityQuery(
                new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)),
            default);

        report.Jurisdictions.Should().ContainSingle();
        report.Jurisdictions[0].Liability.Should().Be(10m); // only the January order
        report.TotalLiability.Should().Be(10m);
    }

    [Fact]
    public async Task ExcludesDraftAndCancelledOrders()
    {
        await SeedCustomerAsync();
        await AddOrderAsync("CA", 0.10m, 100m, status: SalesOrderStatus.Draft);
        await AddOrderAsync("CA", 0.10m, 100m, status: SalesOrderStatus.Cancelled);
        await AddOrderAsync("CA", 0.10m, 100m, status: SalesOrderStatus.Shipped);

        var report = await _handler.Handle(new GetSalesTaxLiabilityQuery(), default);

        report.Jurisdictions.Should().ContainSingle();
        report.Jurisdictions[0].OrderCount.Should().Be(1);
        report.Jurisdictions[0].Liability.Should().Be(10m);
    }

    [Fact]
    public async Task GroupsMissingShipToState_IntoUnassignedRow()
    {
        await SeedCustomerAsync();
        var order = await AddOrderAsync("CA", 0.10m, 100m);
        order.ShipTo!.State = "  "; // whitespace-only → no jurisdiction on record
        await _db.SaveChangesAsync();

        var report = await _handler.Handle(new GetSalesTaxLiabilityQuery(), default);

        report.Jurisdictions.Should().ContainSingle();
        report.Jurisdictions[0].Jurisdiction.Should().BeNull();
        report.Jurisdictions[0].Liability.Should().Be(10m);
    }

    public void Dispose() => _db.Dispose();
}
