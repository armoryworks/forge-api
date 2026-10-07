using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Quotes;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Quotes;

public class ConvertQuoteToOrderIdentityTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IBusinessIdentifierService> _identifiers = new();

    private ConvertQuoteToOrderHandler BuildHandler()
    {
        var http = new Mock<IHttpContextAccessor>();
        http.Setup(x => x.HttpContext).Returns((HttpContext?)null);
        return new ConvertQuoteToOrderHandler(
            new QuoteRepository(_db), new SalesOrderRepository(_db), new BarcodeService(_db, http.Object),
            _db, identifiers: _identifiers.Object);
    }

    private async Task<Quote> SeedAcceptedQuoteAsync(params CustomerAddress[] addresses)
    {
        var customer = new Customer { Name = "Acme Corp" };
        foreach (var address in addresses)
            customer.Addresses.Add(address);
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var quote = new Quote
        {
            QuoteNumber = "QT-0042",
            CustomerId = customer.Id,
            Status = QuoteStatus.Accepted,
            Lines = { new QuoteLine { Description = "Widget", Quantity = 3, UnitPrice = 20m, LineNumber = 1 } },
        };
        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();
        return quote;
    }

    private static CustomerAddress Address(AddressType type, bool isDefault, bool isActive = true) => new()
    {
        Label = type.ToString(),
        AddressType = type,
        Line1 = "1 Main St",
        City = "Springfield",
        State = "UT",
        PostalCode = "84000",
        IsDefault = isDefault,
        IsActive = isActive,
    };

    [Fact]
    public async Task Convert_CreatesSalesOrderBarcode_MatchingOrderNumber()
    {
        var quote = await SeedAcceptedQuoteAsync();

        var result = await BuildHandler().Handle(new ConvertQuoteToOrderCommand(quote.Id), CancellationToken.None);

        var barcode = await _db.Barcodes.SingleAsync(b => b.SalesOrderId == result.Id);
        barcode.EntityType.Should().Be(BarcodeEntityType.SalesOrder);
        barcode.Value.Should().Be(result.OrderNumber);
        barcode.Value.Should().StartWith("SO-");
        barcode.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Convert_IssuesBusinessIdentifier_ForOrderNumber()
    {
        var quote = await SeedAcceptedQuoteAsync();

        var result = await BuildHandler().Handle(new ConvertQuoteToOrderCommand(quote.Id), CancellationToken.None);

        _identifiers.Verify(i => i.IssueAsync(
            BusinessEntityType.SalesOrder, result.Id, result.OrderNumber, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Convert_DefaultsBillingAddress_ToDefaultBillingAddress()
    {
        var both = Address(AddressType.Both, isDefault: true);
        var billing = Address(AddressType.Billing, isDefault: true);
        var quote = await SeedAcceptedQuoteAsync(Address(AddressType.Shipping, isDefault: true), both, billing);

        var result = await BuildHandler().Handle(new ConvertQuoteToOrderCommand(quote.Id), CancellationToken.None);

        var order = await _db.SalesOrders.SingleAsync(o => o.Id == result.Id);
        order.BillingAddressId.Should().Be(billing.Id);
    }

    [Fact]
    public async Task Convert_DefaultsBillingAddress_ToDefaultBothAddress_WhenNoBillingDefault()
    {
        var both = Address(AddressType.Both, isDefault: true);
        var quote = await SeedAcceptedQuoteAsync(
            Address(AddressType.Billing, isDefault: false),
            Address(AddressType.Billing, isDefault: true, isActive: false),
            both);

        var result = await BuildHandler().Handle(new ConvertQuoteToOrderCommand(quote.Id), CancellationToken.None);

        var order = await _db.SalesOrders.SingleAsync(o => o.Id == result.Id);
        order.BillingAddressId.Should().Be(both.Id);
    }

    [Fact]
    public async Task Convert_LeavesBillingAddressEmpty_WhenCustomerHasNoDefault()
    {
        var quote = await SeedAcceptedQuoteAsync(
            Address(AddressType.Billing, isDefault: false),
            Address(AddressType.Shipping, isDefault: true));

        var result = await BuildHandler().Handle(new ConvertQuoteToOrderCommand(quote.Id), CancellationToken.None);

        var order = await _db.SalesOrders.SingleAsync(o => o.Id == result.Id);
        order.BillingAddressId.Should().BeNull();
    }
}
