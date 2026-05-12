using System.Net;
using System.Net.Http.Json;

namespace Forge.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class ApiEndpointTests
{
    private readonly HttpClient _client;

    public ApiEndpointTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ─── Protected GET endpoints return 401 without auth ───

    [Fact]
    public async Task GET_Parts_Returns401_WhenUnauthenticated()
    {
        var response = await _client.GetAsync("/api/v1/parts");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GET_Customers_Returns401_WhenUnauthenticated()
    {
        var response = await _client.GetAsync("/api/v1/customers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GET_Expenses_Returns401_WhenUnauthenticated()
    {
        var response = await _client.GetAsync("/api/v1/expenses");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GET_Assets_Returns401_WhenUnauthenticated()
    {
        var response = await _client.GetAsync("/api/v1/assets");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GET_Leads_Returns401_WhenUnauthenticated()
    {
        var response = await _client.GetAsync("/api/v1/leads");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GET_TimeEntries_Returns401_WhenUnauthenticated()
    {
        var response = await _client.GetAsync("/api/v1/time-tracking/entries");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GET_Invoices_Returns401_WhenUnauthenticated()
    {
        var response = await _client.GetAsync("/api/v1/invoices");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GET_Vendors_Returns401_WhenUnauthenticated()
    {
        var response = await _client.GetAsync("/api/v1/vendors");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GET_PurchaseOrders_Returns401_WhenUnauthenticated()
    {
        var response = await _client.GetAsync("/api/v1/purchase-orders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GET_SalesOrders_Returns401_WhenUnauthenticated()
    {
        var response = await _client.GetAsync("/api/v1/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GET_Quotes_Returns401_WhenUnauthenticated()
    {
        var response = await _client.GetAsync("/api/v1/quotes");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GET_Shipments_Returns401_WhenUnauthenticated()
    {
        var response = await _client.GetAsync("/api/v1/shipments");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ─── POST endpoints with empty body ───

    [Fact]
    public async Task POST_NfcLogin_Returns400_WithEmptyBody()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/nfc-login", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task POST_KioskLogin_Returns400_WithEmptyBody()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/kiosk-login", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ─── Security-hardened endpoints (closed by commit 5255614) ───

    [Fact]
    public async Task GET_ShopFloorDisplay_Returns401_WithoutKioskToken()
    {
        // Shop-floor kiosk endpoints require either admin JWT or
        // X-Kiosk-Device-Token header validated via [KioskTerminalAuth].
        var response = await _client.GetAsync("/api/v1/display/shop-floor");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GET_AccountingMode_Returns401_WhenUnauthenticated()
    {
        // accounting-mode was previously [AllowAnonymous]; locked down to
        // prevent info disclosure about configured accounting providers.
        var response = await _client.GetAsync("/api/v1/admin/accounting-mode");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
