using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Tests.Capabilities;

/// <summary>
/// Phase 4 Phase-D — Smoke tests verifying that representative endpoints across
/// each functional area return 403 with the capability envelope when the relevant
/// capability is disabled. One test per WU (D1..D12) covering the bulk-registered
/// capabilities Phase D added.
///
/// Each test:
///   1. Disables the capability via direct DB write + snapshot refresh (bypasses
///      the dependency-cascade-block so we can isolate the gate behavior even when
///      a capability has enabled dependents).
///   2. Issues an authenticated GET to a representative endpoint on the gated
///      controller.
///   3. Asserts 403 + X-Capability-Disabled header matches the capability code.
///   4. Restores the prior state in finally.
///
/// Pattern mirrors <see cref="CapabilityToggleTests.Capability_Disabled_Endpoint_Returns_403_With_Envelope"/>.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class CapabilityPhaseDSmokeTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;

    public CapabilityPhaseDSmokeTests(CapabilityTestWebApplicationFactory factory)
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

    private async Task<bool> ForceCapabilityStateAsync(string code, bool enabled)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Capabilities.FirstAsync(c => c.Code == code);
        var prior = row.Enabled;
        row.Enabled = enabled;
        await db.SaveChangesAsync();
        var snapshots = scope.ServiceProvider.GetRequiredService<Forge.Api.Capabilities.ICapabilitySnapshotProvider>();
        await snapshots.RefreshAsync();
        return prior;
    }

    private async Task AssertGatedEndpointReturns403Async(string capabilityCode, string endpoint)
    {
        var prior = await ForceCapabilityStateAsync(capabilityCode, false);
        try
        {
            var client = AuthenticatedClient();
            var response = await client.GetAsync(endpoint);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.True(response.Headers.TryGetValues("X-Capability-Disabled", out var hdr));
            Assert.Equal(capabilityCode, hdr!.First());
        }
        finally
        {
            await ForceCapabilityStateAsync(capabilityCode, prior);
        }
    }

    // ── WU-D1: Master data ─────────────────────────────────────────────
    [Fact]
    public Task WU_D1_Employees_Gated_By_CAP_MD_EMPLOYEES()
        => AssertGatedEndpointReturns403Async("CAP-MD-EMPLOYEES", "/api/v1/employees");

    // ── WU-D2: Inventory + lots/serials ─────────────────────────────────
    [Fact]
    public Task WU_D2_Lots_Gated_By_CAP_INV_LOTS()
        => AssertGatedEndpointReturns403Async("CAP-INV-LOTS", "/api/v1/lots");

    // ── WU-D3: Jobs / MFG / shop floor / kanban ─────────────────────────
    [Fact]
    public Task WU_D3_Jobs_Gated_By_CAP_MFG_WO_RELEASE()
        => AssertGatedEndpointReturns403Async("CAP-MFG-WO-RELEASE", "/api/v1/jobs");

    // ── WU-D4: Procurement (P2P) ────────────────────────────────────────
    [Fact]
    public Task WU_D4_Approvals_Gated_By_CAP_P2P_APPROVALS()
        => AssertGatedEndpointReturns403Async("CAP-P2P-APPROVALS", "/api/v1/approvals/pending");

    // ── WU-D5: Sales & fulfillment ──────────────────────────────────────
    [Fact]
    public Task WU_D5_Shipments_Gated_By_CAP_O2C_SHIP()
        => AssertGatedEndpointReturns403Async("CAP-O2C-SHIP", "/api/v1/shipments");

    // ── WU-D6: Quality + compliance forms ───────────────────────────────
    [Fact]
    public Task WU_D6_Spc_Gated_By_CAP_QC_SPC()
        => AssertGatedEndpointReturns403Async("CAP-QC-SPC", "/api/v1/spc/characteristics");

    // ── WU-D7: Maintenance ──────────────────────────────────────────────
    [Fact]
    public Task WU_D7_PredictiveMaintenance_Gated_By_CAP_MAINT_PREDICTIVE()
        => AssertGatedEndpointReturns403Async("CAP-MAINT-PREDICTIVE", "/api/v1/predictions");

    // ── WU-D8: HR / time / training ─────────────────────────────────────
    [Fact]
    public Task WU_D8_TimeTracking_Gated_By_CAP_HR_TIMETRACK()
        => AssertGatedEndpointReturns403Async("CAP-HR-TIMETRACK", "/api/v1/time-tracking/entries");

    // ── WU-D9: Planning ─────────────────────────────────────────────────
    [Fact]
    public Task WU_D9_Mrp_Gated_By_CAP_PLAN_MRP()
        => AssertGatedEndpointReturns403Async("CAP-PLAN-MRP", "/api/v1/mrp/runs");

    // ── WU-D10: Reporting ───────────────────────────────────────────────
    [Fact]
    public Task WU_D10_Reports_Gated_By_CAP_RPT_OPERATIONAL()
        => AssertGatedEndpointReturns403Async("CAP-RPT-OPERATIONAL", "/api/v1/reports/jobs-by-stage");

    // ── WU-D11: Cross-cutting / external integrations ───────────────────
    [Fact]
    public Task WU_D11_Notifications_Gated_By_CAP_CROSS_NOTIFICATIONS()
        => AssertGatedEndpointReturns403Async("CAP-CROSS-NOTIFICATIONS", "/api/v1/notifications");

    // ── WU-D12: EXT + identity + misc ───────────────────────────────────
    [Fact]
    public Task WU_D12_Projects_Gated_By_CAP_EXT_PROJECTS()
        => AssertGatedEndpointReturns403Async("CAP-EXT-PROJECTS", "/api/v1/projects");
}
