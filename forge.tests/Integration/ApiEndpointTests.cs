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

    // ─── RFID-relay token-gated endpoint ───
    //
    // The token-gated zip endpoint is [AllowAnonymous] but still inherits
    // the controller's [RequiresCapability("CAP-CROSS-DOCS")] gate. Two
    // valid failure modes exist for an anonymous unauthorized caller:
    //   • Capability disabled (fresh-install default): 403 from the gate
    //   • Capability enabled, no/bad token: 401 from the controller body
    // Either way the security invariant — anonymous calls without a valid
    // token MUST NOT receive the zip — holds. The tests assert the
    // disjunction rather than pinning the order in which the two
    // mechanisms fire.

    [Fact]
    public async Task GET_RfidRelayViaToken_RefusesAnonymousWithoutHeader()
    {
        var response = await _client.GetAsync("/api/v1/downloads/rfid-relay-via-token.zip");
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"expected 401 or 403, got {(int)response.StatusCode} {response.StatusCode}");
    }

    [Fact]
    public async Task GET_RfidRelayViaToken_RefusesBogusToken()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "/api/v1/downloads/rfid-relay-via-token.zip");
        request.Headers.Add("X-Forge-Download-Token", "dlt_not-a-real-token");
        var response = await _client.SendAsync(request);
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"expected 401 or 403, got {(int)response.StatusCode} {response.StatusCode}");
    }

    [Fact]
    public async Task GET_RfidRelaySetupPs1_RefusesUnauthenticated()
    {
        // The setup-script endpoint is JWT-gated — the threat-model
        // boundary is only between the issuer (admin, JWT) and the
        // workstation installer (anonymous, single-use token).
        var response = await _client.GetAsync("/api/v1/downloads/rfid-relay-setup.ps1");
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"expected 401 or 403, got {(int)response.StatusCode} {response.StatusCode}");
    }
}
