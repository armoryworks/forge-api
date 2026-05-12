using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Tests.Capabilities;

/// <summary>
/// Phase 4 Phase-B — End-to-end integration tests for the gating slice.
/// Exercises the full chain:
///   • <see cref="Forge.Api.Capabilities.RequiresCapabilityAttribute"/>
///     on real controllers (chat, dashboard, customers, etc.)
///   • <c>PUT /api/v1/capabilities/{id}/enabled</c> mutation surface
///   • <see cref="Forge.Api.Capabilities.ICapabilitySnapshotProvider"/>
///     refresh after toggle
///   • <see cref="Forge.Api.Capabilities.CapabilityBootstrapAttribute"/>
///     exempting auth + descriptor + the toggle endpoint itself
///   • <see cref="Forge.Api.Services.ISystemAuditWriter"/> emitting an
///     audit row per mutation
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class CapabilityToggleTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;

    public CapabilityToggleTests(CapabilityTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient AuthenticatedClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private async Task SetCapabilityAsync(string code, bool enabled)
    {
        // Mutate via the admin API so the snapshot refreshes naturally.
        var client = AuthenticatedClient();
        var response = await client.PutAsJsonAsync(
            $"/api/v1/capabilities/{code}/enabled",
            new { enabled });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Phase 4 Phase-C — bypasses the dependency-cascade-block by writing
    /// directly to the DB and refreshing the snapshot. Used by Phase B tests
    /// that need to put the install in a state the admin API would reject
    /// (e.g. disable a capability while its dependents are still enabled,
    /// to verify the gate fires).
    /// </summary>
    private async Task ForceCapabilityStateAsync(string code, bool enabled)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Capabilities.FirstAsync(c => c.Code == code);
        row.Enabled = enabled;
        await db.SaveChangesAsync();
        var snapshots = scope.ServiceProvider.GetRequiredService<Forge.Api.Capabilities.ICapabilitySnapshotProvider>();
        await snapshots.RefreshAsync();
    }

    // ─── Happy path / Gated path / Restore ───

    [Fact]
    public async Task Capability_Enabled_Endpoint_Returns_200_Path()
    {
        // CAP-EXT-CHAT is default-off — explicitly enable it for the happy path.
        await SetCapabilityAsync("CAP-EXT-CHAT", true);

        var client = AuthenticatedClient();
        var response = await client.GetAsync("/api/v1/chat/conversations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Capability_Disabled_Endpoint_Returns_403_With_Envelope()
    {
        // Use the dashboard (default-on) so we know we're observing the gate
        // flipping the behavior, not a default-off pre-condition.
        await SetCapabilityAsync("CAP-RPT-DASHBOARDS", false);
        try
        {
            var client = AuthenticatedClient();
            var response = await client.GetAsync("/api/v1/dashboard");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.True(response.Headers.TryGetValues("X-Capability-Disabled", out var hdrValues));
            Assert.Equal("CAP-RPT-DASHBOARDS", hdrValues!.First());

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var firstError = doc.RootElement.GetProperty("errors")[0];
            Assert.Equal("capability-disabled", firstError.GetProperty("code").GetString());
            Assert.Equal("CAP-RPT-DASHBOARDS", firstError.GetProperty("capability").GetString());
        }
        finally
        {
            // Restore the default-on state so other tests aren't affected.
            await SetCapabilityAsync("CAP-RPT-DASHBOARDS", true);
        }
    }

    [Fact]
    public async Task Capability_Toggle_Off_Then_On_Restores_Endpoint_Access()
    {
        // CAP-MD-CUSTOMERS is default-on with several enabled dependents
        // (CAP-O2C-QUOTE, CAP-O2C-SO, CAP-RPT-OPERATIONAL). Phase C's
        // dependency-cascade-block would reject a direct admin disable here,
        // so we bypass via direct DB write to set up the test state — the
        // verification still asserts the gate behavior end-to-end.
        await ForceCapabilityStateAsync("CAP-MD-CUSTOMERS", false);
        try
        {
            var client = AuthenticatedClient();
            var disabledResponse = await client.GetAsync("/api/v1/customers");
            Assert.Equal(HttpStatusCode.Forbidden, disabledResponse.StatusCode);

            // Restore via the public API (no dependents block enabling).
            await SetCapabilityAsync("CAP-MD-CUSTOMERS", true);

            var enabledResponse = await client.GetAsync("/api/v1/customers");
            Assert.Equal(HttpStatusCode.OK, enabledResponse.StatusCode);
        }
        finally
        {
            // Defensive — make sure default-on state is restored even if the
            // assertion fails in the middle of the round-trip.
            await ForceCapabilityStateAsync("CAP-MD-CUSTOMERS", true);
        }
    }

    // ─── Admin auth required ───

    [Fact]
    public async Task Toggle_Endpoint_Rejects_Non_Admin_Caller_With_403()
    {
        var nonAdmin = AuthenticatedClient(role: "Engineer");
        var response = await nonAdmin.PutAsJsonAsync(
            "/api/v1/capabilities/CAP-EXT-CHAT/enabled",
            new { enabled = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ─── Bootstrap not gated ───

    [Fact]
    public async Task Auth_Login_Endpoint_Is_Reachable_When_All_Capabilities_Disabled()
    {
        // Bulk-disable every gated default-on capability we hold attributes on.
        // /api/v1/auth/login does not have [RequiresCapability] and is NOT
        // mistakenly gated by anything else either — verify it stays reachable.
        // Same reasoning for /api/v1/capabilities/descriptor (bootstrap-marked).
        //
        // Phase C: the public API now blocks disabling capabilities whose
        // dependents are still enabled. We bypass via direct DB write because
        // the test deliberately constructs an unreachable-via-API state.
        var snapshot = SnapshotAllGatedCodes();
        try
        {
            foreach (var code in snapshot.Keys)
                await ForceCapabilityStateAsync(code, false);

            var client = _factory.CreateClient();

            // Login is anonymous — no auth header, no capability gate.
            // We don't care if the credentials are valid; we care that the
            // request reaches a controller (i.e. is not short-circuited by
            // the capability gate). A 400 / 401 is fine; 403 with capability
            // envelope is NOT.
            var loginResp = await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new { Email = "nobody@nowhere.local", Password = "wrong" });

            Assert.NotEqual(HttpStatusCode.Forbidden, loginResp.StatusCode);
            Assert.False(loginResp.Headers.Contains("X-Capability-Disabled"));

            // Descriptor is bootstrap-exempt — verify it still resolves for an
            // authenticated client.
            var authClient = AuthenticatedClient();
            var descResp = await authClient.GetAsync("/api/v1/capabilities/descriptor");
            Assert.Equal(HttpStatusCode.OK, descResp.StatusCode);
        }
        finally
        {
            // Restore every capability to its prior state so subsequent tests
            // see the same baseline.
            foreach (var (code, enabled) in snapshot)
                await ForceCapabilityStateAsync(code, enabled);
        }
    }

    // ─── Snapshot refresh after toggle ───

    [Fact]
    public async Task Descriptor_Reflects_Toggle_Immediately()
    {
        var client = AuthenticatedClient();

        await SetCapabilityAsync("CAP-EXT-CHAT", true);
        var afterEnable = await GetDescriptorEntry(client, "CAP-EXT-CHAT");
        Assert.True(afterEnable.Enabled, "Descriptor should reflect enabled=true immediately after toggle.");

        await SetCapabilityAsync("CAP-EXT-CHAT", false);
        var afterDisable = await GetDescriptorEntry(client, "CAP-EXT-CHAT");
        Assert.False(afterDisable.Enabled, "Descriptor should reflect enabled=false immediately after toggle.");
    }

    // ─── Audit verification ───

    [Fact]
    public async Task Toggle_Writes_Audit_Row_With_Correct_Shape()
    {
        await SetCapabilityAsync("CAP-EXT-CHAT", false);
        try
        {
            // Assert audit history contains both an enabled (the seed/restore)
            // and a disabled (this run) row.
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var capability = await db.Capabilities
                .AsNoTracking()
                .FirstAsync(c => c.Code == "CAP-EXT-CHAT");

            var auditRows = await db.AuditLogEntries
                .AsNoTracking()
                .Where(a => a.EntityType == "Capability" && a.EntityId == capability.Id)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            Assert.NotEmpty(auditRows);
            // The most recent row must be a CapabilityDisabled with our actor +
            // a details JSON containing the code.
            var latest = auditRows[0];
            Assert.Equal("CapabilityDisabled", latest.Action);
            Assert.Equal(1, latest.UserId); // TestAuthHandler claims NameIdentifier = "1".
            Assert.NotNull(latest.Details);
            using var detailsDoc = JsonDocument.Parse(latest.Details!);
            Assert.Equal("CAP-EXT-CHAT", detailsDoc.RootElement.GetProperty("code").GetString());
        }
        finally
        {
            // Restore to default-off; CAP-EXT-CHAT is default-off so leaving
            // disabled is the natural state. (We disabled it inside this test
            // already; a no-op SetCapabilityAsync false would still emit audit,
            // so we just leave it.)
        }
    }

    // ─── Helpers ───

    private async Task<DescriptorEntry> GetDescriptorEntry(HttpClient client, string code)
    {
        var body = await client.GetFromJsonAsync<DescriptorBody>("/api/v1/capabilities/descriptor")
            ?? throw new InvalidOperationException("Descriptor returned null.");
        return body.Capabilities!.First(c => c.Code == code);
    }

    private Dictionary<string, bool> SnapshotAllGatedCodes()
    {
        // The 10 capabilities Phase B applied [RequiresCapability] to. Used by
        // the bootstrap-exemption test to disable everything in one shot.
        var codes = new[]
        {
            "CAP-MD-CUSTOMERS",
            "CAP-MD-VENDORS",
            "CAP-MD-PARTS",
            "CAP-O2C-QUOTE",
            "CAP-O2C-SO",
            "CAP-P2P-PO",
            "CAP-O2C-INVOICE",
            "CAP-O2C-CASH",
            "CAP-RPT-DASHBOARDS",
            "CAP-EXT-CHAT",
        };
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Capabilities
            .AsNoTracking()
            .Where(c => codes.Contains(c.Code))
            .ToDictionary(c => c.Code, c => c.Enabled);
    }

    private record DescriptorBody(
        [property: JsonPropertyName("capabilities")] List<DescriptorEntry>? Capabilities);

    private record DescriptorEntry(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("enabled")] bool Enabled);
}
