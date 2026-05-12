using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Data.Context;

namespace Forge.Tests.Capabilities;

/// <summary>
/// Phase 4 Phase-E — Tests for the three new admin-UI-supporting endpoints:
///   • <c>GET /api/v1/capabilities/{id}/audit-log</c> — scoped audit history
///     for the per-capability detail page.
///   • <c>GET /api/v1/capabilities/{id}/relations</c> — dependency graph
///     (depends-on + depended-by + mutex peers) augmented with current state.
///   • <c>POST /api/v1/capabilities/validate</c> — validate-only bulk-toggle
///     dry-run, returns the same violation shape as bulk-toggle without
///     persisting.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class CapabilityPhaseETests
{
    private readonly CapabilityTestWebApplicationFactory _factory;

    public CapabilityPhaseETests(CapabilityTestWebApplicationFactory factory)
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

    // ─── Audit-log endpoint ─────────────────────────────────────────────────

    [Fact]
    public async Task AuditLog_Returns_Scoped_Entries_For_Capability()
    {
        var client = AuthenticatedClient();

        // Drive at least two audit rows for CAP-EXT-CHAT (default-off).
        await client.PutAsJsonAsync(
            "/api/v1/capabilities/CAP-EXT-CHAT/enabled",
            new { enabled = true, reason = "phase-e-test-1" });
        await client.PutAsJsonAsync(
            "/api/v1/capabilities/CAP-EXT-CHAT/enabled",
            new { enabled = false, reason = "phase-e-test-2" });

        try
        {
            var response = await client.GetAsync("/api/v1/capabilities/CAP-EXT-CHAT/audit-log?take=10");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var rows = await response.Content.ReadFromJsonAsync<List<AuditLogEntryRow>>();
            Assert.NotNull(rows);
            Assert.NotEmpty(rows);

            // Every entry must be scoped to this capability's entity_id.
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var capability = await db.Capabilities.AsNoTracking()
                .FirstAsync(c => c.Code == "CAP-EXT-CHAT");
            Assert.All(rows!, r =>
            {
                Assert.Equal("Capability", r.EntityType);
                Assert.Equal(capability.Id, r.EntityId);
            });

            // Ordered desc by createdAt — first row is the most recent.
            for (var i = 0; i < rows!.Count - 1; i++)
            {
                Assert.True(rows[i].CreatedAt >= rows[i + 1].CreatedAt);
            }

            // The most-recent reason must reflect our second call.
            Assert.Contains(rows!, r =>
                !string.IsNullOrEmpty(r.Details)
                && r.Details.Contains("phase-e-test-2", StringComparison.Ordinal));
        }
        finally
        {
            // Restore default-off (idempotent if already off — still a no-op
            // for the rest of the suite).
        }
    }

    [Fact]
    public async Task AuditLog_Requires_Admin_Role()
    {
        var nonAdmin = AuthenticatedClient(role: "Engineer");
        var response = await nonAdmin.GetAsync("/api/v1/capabilities/CAP-EXT-CHAT/audit-log");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuditLog_Returns_404_For_Unknown_Capability()
    {
        var client = AuthenticatedClient();
        var response = await client.GetAsync("/api/v1/capabilities/CAP-DOES-NOT-EXIST/audit-log");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AuditLog_Honors_Cursor_Before_Param()
    {
        var client = AuthenticatedClient();

        // Drive a few rows.
        await client.PutAsJsonAsync(
            "/api/v1/capabilities/CAP-EXT-PROJECTS/enabled",
            new { enabled = true, reason = "cursor-row-1" });
        await client.PutAsJsonAsync(
            "/api/v1/capabilities/CAP-EXT-PROJECTS/enabled",
            new { enabled = false, reason = "cursor-row-2" });

        try
        {
            var firstPage = await client.GetFromJsonAsync<List<AuditLogEntryRow>>(
                "/api/v1/capabilities/CAP-EXT-PROJECTS/audit-log?take=1");
            Assert.NotNull(firstPage);
            Assert.Single(firstPage!);

            var cursor = firstPage![0].CreatedAt.ToString("O");
            var encoded = Uri.EscapeDataString(cursor);
            var nextPage = await client.GetFromJsonAsync<List<AuditLogEntryRow>>(
                $"/api/v1/capabilities/CAP-EXT-PROJECTS/audit-log?take=10&before={encoded}");
            Assert.NotNull(nextPage);

            // Each row in the next page is strictly older than the cursor.
            Assert.All(nextPage!, r => Assert.True(r.CreatedAt < firstPage![0].CreatedAt));
        }
        finally
        {
            await client.PutAsJsonAsync(
                "/api/v1/capabilities/CAP-EXT-PROJECTS/enabled",
                new { enabled = false });
        }
    }

    // ─── Relations endpoint ────────────────────────────────────────────────

    [Fact]
    public async Task Relations_Returns_Dependencies_And_Dependents_With_State()
    {
        var client = AuthenticatedClient();

        // CAP-O2C-COLLECTIONS depends on CAP-O2C-CASH (default-on).
        // CAP-MD-CUSTOMERS is depended on by several O2C capabilities.
        var response = await client.GetAsync("/api/v1/capabilities/CAP-MD-CUSTOMERS/relations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RelationsBody>();
        Assert.NotNull(body);
        Assert.Equal("CAP-MD-CUSTOMERS", body!.Code);

        // CAP-MD-CUSTOMERS has dependents — at minimum CAP-O2C-QUOTE.
        var dependentCodes = body.Dependents.Select(d => d.Code).ToList();
        Assert.Contains("CAP-O2C-QUOTE", dependentCodes);

        // Each peer carries its current state — CAP-O2C-QUOTE is default-on.
        var quoteEntry = body.Dependents.First(d => d.Code == "CAP-O2C-QUOTE");
        Assert.True(quoteEntry.Enabled);
        Assert.False(string.IsNullOrEmpty(quoteEntry.Name));
        Assert.False(string.IsNullOrEmpty(quoteEntry.Area));
    }

    [Fact]
    public async Task Relations_Returns_Mutex_Peers()
    {
        var client = AuthenticatedClient();
        var response = await client.GetAsync("/api/v1/capabilities/CAP-ACCT-EXTERNAL/relations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RelationsBody>();
        Assert.NotNull(body);

        // The catalog encodes CAP-ACCT-EXTERNAL ⊥ CAP-ACCT-BUILTIN.
        var mutexCodes = body!.Mutexes.Select(m => m.Code).ToList();
        Assert.Contains("CAP-ACCT-BUILTIN", mutexCodes);
    }

    [Fact]
    public async Task Relations_Returns_404_For_Unknown_Capability()
    {
        var client = AuthenticatedClient();
        var response = await client.GetAsync("/api/v1/capabilities/CAP-NOT-A-THING/relations");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ─── Validate endpoint ─────────────────────────────────────────────────

    [Fact]
    public async Task Validate_Happy_Path_Returns_Empty_Violations_And_Does_Not_Persist()
    {
        var client = AuthenticatedClient();

        // Validate enabling CAP-EXT-CHAT (default-off, no missing deps,
        // no mutex peers) — should pass with zero violations.
        var beforeEntry = await GetEntry(client, "CAP-EXT-CHAT");
        Assert.False(beforeEntry.Enabled);

        var response = await client.PostAsJsonAsync(
            "/api/v1/capabilities/validate",
            new
            {
                items = new[]
                {
                    new { id = "CAP-EXT-CHAT", enabled = true },
                },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ValidateBody>();
        Assert.NotNull(body);
        Assert.True(body!.Valid);
        Assert.Empty(body.Violations);

        // Persistence check: state must NOT have changed.
        var afterEntry = await GetEntry(client, "CAP-EXT-CHAT");
        Assert.False(afterEntry.Enabled);
    }

    [Fact]
    public async Task Validate_Returns_Mutex_Violation_Without_Persisting()
    {
        var client = AuthenticatedClient();

        // Validate enabling CAP-ACCT-EXTERNAL while CAP-ACCT-BUILTIN is on.
        var response = await client.PostAsJsonAsync(
            "/api/v1/capabilities/validate",
            new
            {
                items = new[]
                {
                    new { id = "CAP-ACCT-EXTERNAL", enabled = true },
                },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ValidateBody>();
        Assert.NotNull(body);
        Assert.False(body!.Valid);
        Assert.Single(body.Violations);
        Assert.Equal("capability-mutex-violation", body.Violations[0].Code);
        Assert.Contains("CAP-ACCT-BUILTIN", body.Violations[0].Conflicts ?? new List<string>());

        // Persistence check: nothing changed.
        var external = await GetEntry(client, "CAP-ACCT-EXTERNAL");
        Assert.False(external.Enabled);
        var builtin = await GetEntry(client, "CAP-ACCT-BUILTIN");
        Assert.True(builtin.Enabled);
    }

    [Fact]
    public async Task Validate_Returns_Dependents_Block_For_Disable_Of_Master_Data()
    {
        var client = AuthenticatedClient();

        // Validate disabling CAP-MD-CUSTOMERS (which has many dependents).
        var response = await client.PostAsJsonAsync(
            "/api/v1/capabilities/validate",
            new
            {
                items = new[]
                {
                    new { id = "CAP-MD-CUSTOMERS", enabled = false },
                },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ValidateBody>();
        Assert.NotNull(body);
        Assert.False(body!.Valid);
        var violation = body.Violations.First(v => v.Capability == "CAP-MD-CUSTOMERS");
        Assert.Equal("capability-has-dependents", violation.Code);
        Assert.NotNull(violation.Dependents);
        Assert.NotEmpty(violation.Dependents!);
    }

    [Fact]
    public async Task Validate_Whole_Set_Resolves_Dependent_With_Dependency_In_Same_Batch()
    {
        var client = AuthenticatedClient();

        // CAP-O2C-COLLECTIONS depends on CAP-O2C-CASH. If we validate
        // disabling CAP-O2C-CASH AND CAP-O2C-COLLECTIONS in the same batch,
        // the post-apply world has both off — no missing dep, no enabled
        // dependent — so it should pass. (Whole-set semantic per Phase C D4.)
        // Note: CAP-O2C-COLLECTIONS is default-off so its enabled=false toggle
        // is idempotent and skipped.
        var response = await client.PostAsJsonAsync(
            "/api/v1/capabilities/validate",
            new
            {
                items = new[]
                {
                    new { id = "CAP-O2C-COLLECTIONS", enabled = false },
                    new { id = "CAP-O2C-CASH", enabled = false },
                },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ValidateBody>();
        Assert.NotNull(body);
        // CAP-O2C-CASH has dependents beyond Collections (e.g. Invoice → Cash);
        // those still surface. The point of THIS test is that
        // CAP-O2C-COLLECTIONS's idempotent toggle is skipped — no spurious
        // missing-dep violation for the same-batch dependency disable.
        var collectionsViolation = body!.Violations
            .FirstOrDefault(v => v.Capability == "CAP-O2C-COLLECTIONS");
        Assert.Null(collectionsViolation);
    }

    [Fact]
    public async Task Validate_Requires_Admin_Role()
    {
        var nonAdmin = AuthenticatedClient(role: "Engineer");
        var response = await nonAdmin.PostAsJsonAsync(
            "/api/v1/capabilities/validate",
            new
            {
                items = new[] { new { id = "CAP-EXT-CHAT", enabled = true } },
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

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
        [property: JsonPropertyName("enabled")] bool Enabled);

    private record AuditLogEntryRow(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("userId")] int UserId,
        [property: JsonPropertyName("userName")] string UserName,
        [property: JsonPropertyName("action")] string Action,
        [property: JsonPropertyName("entityType")] string? EntityType,
        [property: JsonPropertyName("entityId")] int? EntityId,
        [property: JsonPropertyName("details")] string? Details,
        [property: JsonPropertyName("ipAddress")] string? IpAddress,
        [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

    private record RelationsBody(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("dependencies")] List<RelationEntry> Dependencies,
        [property: JsonPropertyName("dependents")] List<RelationEntry> Dependents,
        [property: JsonPropertyName("mutexes")] List<RelationEntry> Mutexes);

    private record RelationEntry(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("area")] string Area,
        [property: JsonPropertyName("enabled")] bool Enabled);

    private record ValidateBody(
        [property: JsonPropertyName("valid")] bool Valid,
        [property: JsonPropertyName("violations")] List<ValidationViolationItem> Violations);

    private record ValidationViolationItem(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("capability")] string Capability,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("missing")] List<string>? Missing,
        [property: JsonPropertyName("conflicts")] List<string>? Conflicts,
        [property: JsonPropertyName("dependents")] List<string>? Dependents);
}
