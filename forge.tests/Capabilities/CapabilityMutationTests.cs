using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Data.Context;

namespace Forge.Tests.Capabilities;

/// <summary>
/// Phase 4 Phase-C — Tests for the extended mutation surface:
///   • Optimistic concurrency via If-Match (412 on stale, fresh ETag returned).
///   • Dependency-cascade-block on disable (409 + dependents listed).
///   • Dependency-check on enable (409 + missing-deps listed).
///   • Soft-mutex check on enable (409 + conflicts listed).
///   • Config PUT endpoint (200 + audit row, 412 on stale ETag).
///   • Bulk-toggle endpoint (atomic; whole-set validation).
///   • Audit content (before/after captured correctly).
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class CapabilityMutationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;

    public CapabilityMutationTests(CapabilityTestWebApplicationFactory factory)
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

    // ─── Optimistic concurrency ─────────────────────────────────────────────

    [Fact]
    public async Task Toggle_With_Stale_IfMatch_Returns_412()
    {
        var client = AuthenticatedClient();

        // Capture an ETag, then mutate the row twice so the captured one is stale.
        var initial = await GetEntry(client, "CAP-EXT-CHAT");
        await SetCapabilityAsync(client, "CAP-EXT-CHAT", true);
        await SetCapabilityAsync(client, "CAP-EXT-CHAT", false);

        var req = new HttpRequestMessage(HttpMethod.Put, "/api/v1/capabilities/CAP-EXT-CHAT/enabled")
        {
            Content = JsonContent.Create(new { enabled = true }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", initial.ETag);

        var response = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var firstErr = doc.RootElement.GetProperty("errors")[0];
        Assert.Equal("version-mismatch", firstErr.GetProperty("code").GetString());
        Assert.Equal("CAP-EXT-CHAT", firstErr.GetProperty("capability").GetString());
    }

    [Fact]
    public async Task Toggle_With_Fresh_IfMatch_Returns_200_And_New_ETag()
    {
        var client = AuthenticatedClient();

        // Reset to a known state.
        await SetCapabilityAsync(client, "CAP-EXT-CHAT", false);
        var current = await GetEntry(client, "CAP-EXT-CHAT");

        var req = new HttpRequestMessage(HttpMethod.Put, "/api/v1/capabilities/CAP-EXT-CHAT/enabled")
        {
            Content = JsonContent.Create(new { enabled = true }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", current.ETag);

        var response = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("ETag", out var etagValues));
        var newEtag = etagValues!.First();
        Assert.NotEqual(current.ETag, newEtag);
        Assert.StartsWith("W/\"", newEtag);

        // Cleanup: restore default-off.
        await SetCapabilityAsync(client, "CAP-EXT-CHAT", false);
    }

    // ─── Dependency-cascade-block on disable ────────────────────────────────

    [Fact]
    public async Task Disable_With_Enabled_Dependents_Returns_409()
    {
        var client = AuthenticatedClient();

        // CAP-MD-CUSTOMERS is depended-on by CAP-MD-PRICELIST + others. CAP-O2C-QUOTE
        // (default-on) depends on CAP-MD-CUSTOMERS. Disabling CAP-MD-CUSTOMERS while
        // CAP-O2C-QUOTE is still enabled must 409.
        var response = await client.PutAsJsonAsync(
            "/api/v1/capabilities/CAP-MD-CUSTOMERS/enabled",
            new { enabled = false });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var err = doc.RootElement.GetProperty("errors")[0];
        Assert.Equal("capability-has-dependents", err.GetProperty("code").GetString());
        Assert.Equal("CAP-MD-CUSTOMERS", err.GetProperty("capability").GetString());
        var dependents = err.GetProperty("dependents").EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();
        Assert.Contains("CAP-O2C-QUOTE", dependents);
    }

    // ─── Dependency-check on enable ─────────────────────────────────────────

    [Fact]
    public async Task Enable_With_Missing_Dependency_Returns_409()
    {
        var client = AuthenticatedClient();

        // CAP-PLAN-MRP requires CAP-MD-PARTS, CAP-MD-BOM, CAP-INV-CORE — all
        // default-on. Force a missing-dep scenario by disabling CAP-INV-CORE
        // momentarily isn't safe (lots of dependents). Easier: use
        // CAP-EXT-AI-ASSISTANT which depends on CAP-CROSS-ATTACHMENTS — when
        // we disable CAP-CROSS-ATTACHMENTS first and then try to enable
        // CAP-EXT-AI-ASSISTANT, the dependency-missing path fires.
        //
        // CAP-CROSS-ATTACHMENTS itself doesn't have direct dependencies in
        // our graph (we don't model "CROSS-ATTACHMENTS depends on USERS" as
        // a hard edge), so we can disable it safely.

        // Take a snapshot of attachments dependents to see what would block;
        // for this test we use a dependency edge we know is structurally
        // simple. CAP-INV-RESERVE depends on CAP-INV-CORE + CAP-O2C-SO; if
        // we re-enable CAP-INV-RESERVE while CAP-O2C-SO is enabled the
        // dependency check passes — that's the happy path. To hit the
        // missing-dep path, we'd need to disable CAP-O2C-SO first, which
        // also has dependents. Easiest: pick a leaf capability whose deps
        // are easily flippable.
        //
        // Strategy: use CAP-PLAN-MRP (default-off, leaf-ish in the disable
        // direction) and pre-disable CAP-INV-CORE through a back-door — but
        // that has many dependents too.
        //
        // Cleanest path: use CAP-O2C-COLLECTIONS (default-off) which depends
        // on CAP-O2C-CASH. CAP-O2C-CASH is default-on and has 1 dependent
        // (CAP-O2C-COLLECTIONS itself). Since CAP-O2C-COLLECTIONS is off,
        // disabling CAP-O2C-CASH should succeed (no enabled dependent), and
        // then enabling CAP-O2C-COLLECTIONS will hit the missing-dep path.

        await SetCapabilityAsync(client, "CAP-O2C-CASH", false);
        try
        {
            var response = await client.PutAsJsonAsync(
                "/api/v1/capabilities/CAP-O2C-COLLECTIONS/enabled",
                new { enabled = true });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var err = doc.RootElement.GetProperty("errors")[0];
            Assert.Equal("capability-missing-dependencies", err.GetProperty("code").GetString());
            Assert.Equal("CAP-O2C-COLLECTIONS", err.GetProperty("capability").GetString());
            var missing = err.GetProperty("missing").EnumerateArray()
                .Select(e => e.GetString()!)
                .ToList();
            Assert.Contains("CAP-O2C-CASH", missing);
        }
        finally
        {
            await SetCapabilityAsync(client, "CAP-O2C-CASH", true);
        }
    }

    [Fact]
    public async Task Enable_With_All_Dependencies_Satisfied_Returns_200()
    {
        var client = AuthenticatedClient();

        // CAP-O2C-COLLECTIONS depends on CAP-O2C-CASH (default-on). Enabling
        // it while CAP-O2C-CASH is enabled must succeed.
        try
        {
            var response = await client.PutAsJsonAsync(
                "/api/v1/capabilities/CAP-O2C-COLLECTIONS/enabled",
                new { enabled = true });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            // Restore default-off.
            await SetCapabilityAsync(client, "CAP-O2C-COLLECTIONS", false);
        }
    }

    // ─── Soft-mutex check on enable ─────────────────────────────────────────

    [Fact]
    public async Task Enable_With_Mutex_Peer_Enabled_Returns_409()
    {
        var client = AuthenticatedClient();

        // CAP-ACCT-BUILTIN is default-on; CAP-ACCT-EXTERNAL is its mutex peer
        // and default-off. Trying to enable CAP-ACCT-EXTERNAL while
        // CAP-ACCT-BUILTIN is enabled must 409.
        var response = await client.PutAsJsonAsync(
            "/api/v1/capabilities/CAP-ACCT-EXTERNAL/enabled",
            new { enabled = true });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var err = doc.RootElement.GetProperty("errors")[0];
        Assert.Equal("capability-mutex-violation", err.GetProperty("code").GetString());
        Assert.Equal("CAP-ACCT-EXTERNAL", err.GetProperty("capability").GetString());
        var conflicts = err.GetProperty("conflicts").EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();
        Assert.Contains("CAP-ACCT-BUILTIN", conflicts);
    }

    // ─── Config endpoint ────────────────────────────────────────────────────

    [Fact]
    public async Task Config_Put_With_Valid_Body_Returns_200_And_Writes_Audit_Row()
    {
        var client = AuthenticatedClient();
        var payload = "{\"key\":\"value-" + Guid.NewGuid().ToString("N")[..8] + "\"}";

        var response = await client.PutAsJsonAsync(
            "/api/v1/capabilities/CAP-EXT-CHAT/config",
            new { configJson = payload, reason = "test edit" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("ETag", out var etag));
        Assert.StartsWith("W/\"", etag!.First());

        // Audit row written.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var capability = await db.Capabilities.AsNoTracking()
            .FirstAsync(c => c.Code == "CAP-EXT-CHAT");
        var audit = await db.AuditLogEntries.AsNoTracking()
            .Where(a => a.EntityType == "Capability"
                && a.EntityId == capability.Id
                && a.Action == "CapabilityConfigChanged")
            .OrderByDescending(a => a.CreatedAt)
            .FirstAsync();
        Assert.NotNull(audit.Details);
        using var detailsDoc = JsonDocument.Parse(audit.Details!);
        var afterJsonElement = detailsDoc.RootElement.GetProperty("after").GetProperty("configJson");
        // The serialized "after.configJson" is the raw config string we just set.
        Assert.Contains("value-", afterJsonElement.GetString());
    }

    [Fact]
    public async Task Config_Put_With_Stale_IfMatch_Returns_412()
    {
        var client = AuthenticatedClient();

        // First write to create the config row.
        await client.PutAsJsonAsync(
            "/api/v1/capabilities/CAP-EXT-CHAT/config",
            new { configJson = "{\"v\":1}" });
        var first = await GetEntry(client, "CAP-EXT-CHAT");

        // Mutate again without If-Match — bumps the version, making first.ConfigETag stale.
        await client.PutAsJsonAsync(
            "/api/v1/capabilities/CAP-EXT-CHAT/config",
            new { configJson = "{\"v\":2}" });

        var staleReq = new HttpRequestMessage(HttpMethod.Put, "/api/v1/capabilities/CAP-EXT-CHAT/config")
        {
            Content = JsonContent.Create(new { configJson = "{\"v\":3}" }),
        };
        staleReq.Headers.TryAddWithoutValidation("If-Match", first.ConfigETag!);

        var response = await client.SendAsync(staleReq);
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    // ─── Bulk-toggle endpoint ───────────────────────────────────────────────

    [Fact]
    public async Task Bulk_Toggle_All_Succeed_Returns_200()
    {
        var client = AuthenticatedClient();

        // Two default-off capabilities with no entangling dependencies in the
        // current state. Toggle both on.
        var body = new
        {
            items = new[]
            {
                new { id = "CAP-EXT-CHAT", enabled = true },
                new { id = "CAP-EXT-PROJECTS", enabled = true },
            },
            reason = "bulk smoke",
        };

        try
        {
            var response = await client.PostAsJsonAsync("/api/v1/capabilities/bulk-toggle", body);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var ext = await GetEntry(client, "CAP-EXT-CHAT");
            Assert.True(ext.Enabled);
            var proj = await GetEntry(client, "CAP-EXT-PROJECTS");
            Assert.True(proj.Enabled);
        }
        finally
        {
            await SetCapabilityAsync(client, "CAP-EXT-CHAT", false);
            await SetCapabilityAsync(client, "CAP-EXT-PROJECTS", false);
        }
    }

    [Fact]
    public async Task Bulk_Toggle_With_Any_Violation_Rolls_Back_Atomically()
    {
        var client = AuthenticatedClient();

        // Mix a valid toggle (enable CAP-EXT-CHAT) with an invalid one (enable
        // CAP-ACCT-EXTERNAL while CAP-ACCT-BUILTIN is enabled — mutex). The
        // invalid one must fail the whole batch; the valid one must NOT
        // persist (atomic rollback).
        var before = await GetEntry(client, "CAP-EXT-CHAT");
        Assert.False(before.Enabled);

        var body = new
        {
            items = new[]
            {
                new { id = "CAP-EXT-CHAT", enabled = true },
                new { id = "CAP-ACCT-EXTERNAL", enabled = true },
            },
        };

        var response = await client.PostAsJsonAsync("/api/v1/capabilities/bulk-toggle", body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var bodyText = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(bodyText);
        var err = doc.RootElement.GetProperty("errors")[0];
        Assert.Equal("bulk-validation-failed", err.GetProperty("code").GetString());
        var violations = err.GetProperty("violations").EnumerateArray().ToList();
        Assert.NotEmpty(violations);

        // Rollback assertion: CAP-EXT-CHAT must STILL be disabled.
        var after = await GetEntry(client, "CAP-EXT-CHAT");
        Assert.False(after.Enabled);
    }

    // ─── Audit content ──────────────────────────────────────────────────────

    [Fact]
    public async Task Toggle_Audit_Row_Captures_Before_And_After_State()
    {
        var client = AuthenticatedClient();
        // Force a known transition: ensure the row is currently disabled, then enable.
        await SetCapabilityAsync(client, "CAP-EXT-CHAT", false);

        // Trigger an enable — this is the row whose audit details we'll read.
        var resp = await client.PutAsJsonAsync(
            "/api/v1/capabilities/CAP-EXT-CHAT/enabled",
            new { enabled = true, reason = "audit-shape-test" });
        resp.EnsureSuccessStatusCode();

        try
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var capability = await db.Capabilities.AsNoTracking()
                .FirstAsync(c => c.Code == "CAP-EXT-CHAT");
            var audit = await db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == "Capability"
                    && a.EntityId == capability.Id
                    && a.Action == "CapabilityEnabled")
                .OrderByDescending(a => a.CreatedAt)
                .FirstAsync();
            Assert.NotNull(audit.Details);
            using var detailsDoc = JsonDocument.Parse(audit.Details!);
            var root = detailsDoc.RootElement;

            Assert.Equal("CAP-EXT-CHAT", root.GetProperty("code").GetString());
            Assert.False(root.GetProperty("from").GetBoolean());
            Assert.True(root.GetProperty("to").GetBoolean());
            Assert.False(root.GetProperty("before").GetProperty("enabled").GetBoolean());
            Assert.True(root.GetProperty("after").GetProperty("enabled").GetBoolean());
            Assert.Equal("audit-shape-test", root.GetProperty("reason").GetString());
            Assert.Equal(1, root.GetProperty("actorUserId").GetInt32());
        }
        finally
        {
            await SetCapabilityAsync(client, "CAP-EXT-CHAT", false);
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static async Task SetCapabilityAsync(HttpClient client, string code, bool enabled)
    {
        var resp = await client.PutAsJsonAsync(
            $"/api/v1/capabilities/{code}/enabled",
            new { enabled });
        resp.EnsureSuccessStatusCode();
    }

    private async Task<DescriptorEntry> GetEntry(HttpClient client, string code)
    {
        var body = await client.GetFromJsonAsync<DescriptorBody>("/api/v1/capabilities/descriptor")
            ?? throw new InvalidOperationException("Descriptor returned null.");
        return body.Capabilities!.First(c => c.Code == code);
    }

    private record DescriptorBody(
        [property: JsonPropertyName("capabilities")] List<DescriptorEntry>? Capabilities);

    private record DescriptorEntry(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("version")] uint Version,
        [property: JsonPropertyName("eTag")] string ETag,
        [property: JsonPropertyName("configETag")] string? ConfigETag);
}
