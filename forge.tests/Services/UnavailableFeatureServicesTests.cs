using System.Text.Json;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Features.Cpq;
using Forge.Api.Features.SalesOrders;
using Forge.Api.Middleware;
using Forge.Api.Services.Unavailable;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

public class UnavailableFeatureServicesTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task SeedAsync()
    {
        _db.SalesOrders.Add(new SalesOrder { Id = 3, OrderNumber = "SO-00003", CustomerId = 1, Status = SalesOrderStatus.Confirmed });
        _db.SalesOrderLines.Add(new SalesOrderLine { Id = 30, SalesOrderId = 3, Description = "Valve", Quantity = 2, UnitPrice = 10m, LineNumber = 1 });
        _db.ProductConfigurations.Add(new ProductConfiguration { Id = 8, ConfiguratorId = 1, ConfigurationCode = "CFG-8" });
        await _db.SaveChangesAsync();
    }

    private static async Task<(int Status, JsonElement Body)> RunMiddleware(Exception thrown)
    {
        var middleware = new ExceptionHandlingMiddleware(_ => throw thrown, NullLogger<ExceptionHandlingMiddleware>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(ctx);
        ctx.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(ctx.Response.Body);
        return (ctx.Response.StatusCode, doc.RootElement.Clone());
    }

    [Fact]
    public async Task Creating_a_drop_ship_order_is_not_available_and_returns_no_purchase_order()
    {
        await SeedAsync();
        var handler = new CreateDropShipOrderHandler(_db, new UnavailableDropShipService());

        var act = () => handler.Handle(
            new CreateDropShipOrderCommand(3, 30, new CreateDropShipRequestModel { VendorId = 4 }), CancellationToken.None);

        await act.Should().ThrowAsync<FeatureNotAvailableException>().WithMessage(UnavailableDropShipService.Message);
        _db.PurchaseOrders.Should().BeEmpty();
    }

    [Fact]
    public async Task Creating_a_back_to_back_order_is_not_available()
    {
        await SeedAsync();
        var handler = new CreateBackToBackOrderHandler(_db, new UnavailableBackToBackService());

        var act = () => handler.Handle(
            new CreateBackToBackOrderCommand(3, 30, new CreateBackToBackRequestModel { VendorId = 4 }), CancellationToken.None);

        await act.Should().ThrowAsync<FeatureNotAvailableException>().WithMessage(UnavailableBackToBackService.Message);
    }

    [Fact]
    public async Task Generating_a_quote_from_a_configuration_is_not_available_and_leaves_it_unquoted()
    {
        await SeedAsync();
        var handler = new GenerateQuoteFromConfigHandler(_db, new UnavailableCpqService());

        var act = () => handler.Handle(
            new GenerateQuoteFromConfigCommand(8, new GenerateQuoteFromConfigRequestModel(1)), CancellationToken.None);

        await act.Should().ThrowAsync<FeatureNotAvailableException>();
        _db.ProductConfigurations.Single().QuoteId.Should().BeNull();
    }

    [Fact]
    public async Task Pending_lists_are_empty_rather_than_refused()
    {
        (await new UnavailableDropShipService().GetPendingDropShipsAsync(CancellationToken.None)).Should().BeEmpty();
        (await new UnavailableBackToBackService().GetPendingBackToBacksAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Not_available_is_a_501_problem()
    {
        var (status, body) = await RunMiddleware(new FeatureNotAvailableException(UnavailableCpqService.Message));

        status.Should().Be(StatusCodes.Status501NotImplemented);
        body.GetProperty("title").GetString().Should().Be("Not available");
        body.GetProperty("detail").GetString().Should().Be(UnavailableCpqService.Message);
        body.GetProperty("code").GetString().Should().Be("not-available");
    }

    [Fact]
    public async Task A_mock_that_is_not_implemented_is_a_501_without_its_internal_message()
    {
        var (status, body) = await RunMiddleware(new NotImplementedException("Mock CPQ does not generate real quotes"));

        status.Should().Be(StatusCodes.Status501NotImplemented);
        body.GetProperty("detail").GetString().Should().Be("This feature is not available in this version of Forge.");
    }
}
