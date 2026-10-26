using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.RetailOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.RetailOrders;

public class CreateRetailOrderTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private SalesChannel _channel = null!;

    private async Task SeedAsync(string? prefix)
    {
        var house = new Customer { Name = "Web store house account" };
        _db.Customers.Add(house);
        await _db.SaveChangesAsync();
        _channel = new SalesChannel
        {
            Name = "Web Store", Code = "WEB", ChannelType = SalesChannelType.DirectRetail,
            SoldToCustomerId = house.Id, OrderNumberPrefix = prefix,
        };
        _db.SalesChannels.Add(_channel);
        _db.Parts.Add(new Part { PartNumber = "SP-KIT-01", Name = "Valve Service Kit" });
        await _db.SaveChangesAsync();
    }

    private CreateRetailOrderHandler Handler()
    {
        var resolver = new Mock<ISalesChannelResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(_channel);
        resolver.Setup(r => r.ResolveSoldToCustomerIdAsync(It.IsAny<SalesChannel>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_channel.SoldToCustomerId!.Value);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 14, 15, 0, 0, TimeSpan.Zero));
        return new CreateRetailOrderHandler(
            _db, resolver.Object, new SalesOrderRepository(_db), Mock.Of<IBarcodeService>(),
            Mock.Of<IBusinessIdentifierService>(), clock.Object);
    }

    private static CreateRetailOrderCommand Order(string sku) => new(new CreateRetailOrderRequestModel
    {
        Buyer = new RetailBuyerInput { DisplayName = "Phone buyer" },
        ShipTo = new OrderShipToInput { Name = "Phone buyer", Line1 = "1 Main", City = "Provo", State = "UT", PostalCode = "84601" },
        Lines = [new CreateRetailOrderLineModel { ExternalSku = sku, Description = "Valve Service Kit", Quantity = 2m, UnitPrice = 45m }],
    });

    [Fact]
    public async Task Sku_without_a_listing_links_to_the_part_with_that_part_number_ignoring_case()
    {
        await SeedAsync("WEB");
        var partId = (await _db.Parts.SingleAsync()).Id;

        var result = await Handler().Handle(Order("sp-kit-01"), default);

        var line = await _db.SalesOrderLines.SingleAsync(l => l.SalesOrderId == result.Order.Id);
        line.PartId.Should().Be(partId);
    }

    [Fact]
    public async Task Channel_listing_still_wins_over_the_part_number()
    {
        await SeedAsync("WEB");
        var listed = new Part { PartNumber = "SP-KIT-02", Name = "Listed kit" };
        _db.Parts.Add(listed);
        await _db.SaveChangesAsync();
        _db.ChannelListings.Add(new ChannelListing { ChannelId = _channel.Id, ExternalListingId = "L1", ExternalSku = "SP-KIT-01", PartId = listed.Id });
        await _db.SaveChangesAsync();

        var result = await Handler().Handle(Order("SP-KIT-01"), default);

        (await _db.SalesOrderLines.SingleAsync(l => l.SalesOrderId == result.Order.Id)).PartId.Should().Be(listed.Id);
    }

    [Fact]
    public async Task Unknown_sku_still_lands_as_an_unmapped_line()
    {
        await SeedAsync("WEB");

        var result = await Handler().Handle(Order("NOPE-1"), default);

        (await _db.SalesOrderLines.SingleAsync(l => l.SalesOrderId == result.Order.Id)).PartId.Should().BeNull();
    }

    [Theory]
    [InlineData("WEB-")]
    [InlineData(" web ")]
    public async Task Order_number_has_a_single_dash_after_the_prefix(string prefix)
    {
        await SeedAsync(prefix);

        var result = await Handler().Handle(Order("SP-KIT-01"), default);

        result.Order.OrderNumber.Should().Be("WEB-00001");
    }

    [Theory]
    [InlineData("WEB-", "WEB")]
    [InlineData(" eb-- ", "EB")]
    [InlineData("-", null)]
    [InlineData(null, null)]
    public void Prefix_is_normalized_without_trailing_dashes(string? input, string? expected)
        => SalesChannel.NormalizeOrderNumberPrefix(input).Should().Be(expected);

    public void Dispose() => _db.Dispose();
}
