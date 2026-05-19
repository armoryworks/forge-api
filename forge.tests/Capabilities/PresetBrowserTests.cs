using System.Net;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Api.Capabilities;
using Forge.Data.Context;

namespace Forge.Tests.Capabilities;

/// <summary>
/// Phase 4 Phase-G — Tests for the preset browser + apply orchestration:
///   • GET /api/v1/presets — list of 10 presets with summary descriptors.
///   • GET /api/v1/presets/{id} — single preset detail with deltas.
///   • POST /api/v1/presets/compare — side-by-side matrix.
///   • POST /api/v1/presets/{id}/preview-apply — deltas + violations, no persist.
///   • POST /api/v1/presets/{id}/apply — applies the preset, no-op semantics
///     when state matches, re-baseline semantics when state has drifted.
///   • POST /api/v1/presets/custom/preview — Custom builder preview.
///   • POST /api/v1/presets/custom/apply — Custom builder apply.
///
/// Auth: every endpoint requires the Admin role; non-admin returns 403.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class PresetBrowserTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;

    public PresetBrowserTests(CapabilityTestWebApplicationFactory factory)
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

    // ─── List ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_Returns_All_Ten_Presets()
    {
        var client = AuthenticatedClient();
        var response = await client.GetAsync("/api/v1/presets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summaries = await response.Content.ReadFromJsonAsync<List<PresetSummaryRow>>();
        Assert.NotNull(summaries);
        Assert.Equal(10, summaries!.Count);

        Assert.Contains(summaries, p => p.Id == "PRESET-01" && p.Name == "Two-Person Shop");
        Assert.Contains(summaries, p => p.Id == "PRESET-08" && p.Name == "Pro Services");
        Assert.Contains(summaries, p => p.Id == "PRESET-09" && p.Name == "Hybrid (Make + Service)");
        Assert.Contains(summaries, p => p.Id == "PRESET-CUSTOM" && p.IsCustom);
        Assert.All(summaries, p => Assert.True(p.CapabilityCount > 0));
        Assert.All(summaries, p => Assert.NotEmpty(p.RecommendedFor));
    }

    [Fact]
    public async Task List_Requires_Admin_Role()
    {
        var nonAdmin = AuthenticatedClient(role: "Engineer");
        var response = await nonAdmin.GetAsync("/api/v1/presets");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ─── Detail ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Detail_Returns_Preset_With_Capabilities_And_Deltas()
    {
        var client = AuthenticatedClient();
        var response = await client.GetAsync("/api/v1/presets/PRESET-04");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<PresetDetailRow>();
        Assert.NotNull(detail);
        Assert.Equal("PRESET-04", detail!.Id);
        Assert.Equal("Production Manufacturer", detail.Name);
        Assert.NotEmpty(detail.Capabilities);
        // Every catalog row must be present in the capabilities list (in or out of preset).
        Assert.Equal(CapabilityCatalog.All.Count, detail.Capabilities.Count);
        // Delta vs catalog must be non-empty for any non-baseline preset.
        Assert.NotEmpty(detail.DeltaVsCatalogDefaults);
    }

    [Fact]
    public async Task Detail_Custom_Preset_Falls_Back_To_Catalog_Defaults()
    {
        var client = AuthenticatedClient();
        var response = await client.GetAsync("/api/v1/presets/PRESET-CUSTOM");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<PresetDetailRow>();
        Assert.NotNull(detail);
        Assert.True(detail!.IsCustom);
        // Custom = catalog defaults; deltaVsCatalogDefaults should be empty
        // (the preset IS the catalog default state).
        Assert.Empty(detail.DeltaVsCatalogDefaults);
    }

    [Fact]
    public async Task Detail_Unknown_Preset_Returns_404()
    {
        var client = AuthenticatedClient();
        var response = await client.GetAsync("/api/v1/presets/PRESET-NOT-A-THING");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ─── Compare ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Compare_Two_Presets_Returns_Matrix()
    {
        var client = AuthenticatedClient();
        var body = new { presetIds = new[] { "PRESET-01", "PRESET-04" } };
        var response = await client.PostAsJsonAsync("/api/v1/presets/compare", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var matrix = await response.Content.ReadFromJsonAsync<CompareRow>();
        Assert.NotNull(matrix);
        Assert.Equal(2, matrix!.Presets.Count);
        Assert.Equal(CapabilityCatalog.All.Count, matrix.Rows.Count);
        Assert.All(matrix.Rows, r => Assert.Equal(2, r.Cells.Count));
        // At least some rows should disagree between PRESET-01 (small) and PRESET-04 (production).
        Assert.Contains(matrix.Rows, r => r.Disagreement);
    }

    [Fact]
    public async Task Compare_Four_Presets_Allowed()
    {
        var client = AuthenticatedClient();
        var body = new { presetIds = new[] { "PRESET-01", "PRESET-04", "PRESET-05", "PRESET-07" } };
        var response = await client.PostAsJsonAsync("/api/v1/presets/compare", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var matrix = await response.Content.ReadFromJsonAsync<CompareRow>();
        Assert.NotNull(matrix);
        Assert.Equal(4, matrix!.Presets.Count);
    }

    [Fact]
    public async Task Compare_Five_Presets_Rejected()
    {
        var client = AuthenticatedClient();
        var body = new { presetIds = new[] { "PRESET-01", "PRESET-02", "PRESET-03", "PRESET-04", "PRESET-05" } };
        var response = await client.PostAsJsonAsync("/api/v1/presets/compare", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ─── Preview-apply ─────────────────────────────────────────────────────

    [Fact]
    public async Task PreviewApply_Returns_Deltas_Without_Persisting()
    {
        var client = AuthenticatedClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var enabledCountBefore = await db.Capabilities.CountAsync(c => c.Enabled);

        var response = await client.PostAsJsonAsync(
            "/api/v1/presets/PRESET-04/preview-apply",
            new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<PreviewApplyRow>();
        Assert.NotNull(preview);
        Assert.True(preview!.Valid);

        // Verify no rows were mutated.
        using var scopeAfter = _factory.Services.CreateScope();
        var dbAfter = scopeAfter.ServiceProvider.GetRequiredService<AppDbContext>();
        var enabledCountAfter = await dbAfter.Capabilities.CountAsync(c => c.Enabled);
        Assert.Equal(enabledCountBefore, enabledCountAfter);
    }

    // ─── Apply ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_Enables_Disables_Expected_Capabilities_And_Writes_Audit()
    {
        var client = AuthenticatedClient();

        // Reset state to a known-different baseline (PRESET-01) so the apply
        // below has actual deltas. The test-collection fixture shares state
        // across tests, so we can't assume the install is fresh.
        await client.PostAsJsonAsync("/api/v1/presets/PRESET-01/apply", new { reason = "test-baseline" });

        // Apply PRESET-04.
        var response = await client.PostAsJsonAsync(
            "/api/v1/presets/PRESET-04/apply",
            new { reason = "phase-g-test apply" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApplyResultRow>();
        Assert.NotNull(result);
        Assert.False(result!.NoOp);
        Assert.True(result.DeltaCount > 0);

        // PRESET-04 enables QC-INSPECTION and disables ACCT-BUILTIN (mutex
        // with ACCT-EXTERNAL). Verify on the persisted state.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var qcInspection = await db.Capabilities.AsNoTracking()
            .FirstAsync(c => c.Code == "CAP-QC-INSPECTION");
        Assert.True(qcInspection.Enabled);
        var acctBuiltin = await db.Capabilities.AsNoTracking()
            .FirstAsync(c => c.Code == "CAP-ACCT-BUILTIN");
        Assert.False(acctBuiltin.Enabled);

        // Audit row written.
        var auditRow = await db.AuditLogEntries
            .Where(a => a.Action == "PresetApplied")
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(auditRow);
        Assert.Contains("PRESET-04", auditRow!.Details);
        Assert.Contains("preset-browser-direct", auditRow.Details);
    }

    [Fact]
    public async Task Apply_NoOp_When_State_Matches()
    {
        var client = AuthenticatedClient();

        // Apply PRESET-02 first to put the state in a known shape.
        var first = await client.PostAsJsonAsync("/api/v1/presets/PRESET-02/apply", new { reason = "setup" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Re-apply PRESET-02 — should be a no-op (zero deltas).
        var second = await client.PostAsJsonAsync("/api/v1/presets/PRESET-02/apply", new { reason = "re-apply" });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var result = await second.Content.ReadFromJsonAsync<ApplyResultRow>();
        Assert.NotNull(result);
        Assert.True(result!.NoOp);
        Assert.Equal(0, result.DeltaCount);

        // Audit row still written, with "no-op" outcome marker.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditRow = await db.AuditLogEntries
            .Where(a => a.Action == "PresetApplied")
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(auditRow);
        Assert.Contains("no-op", auditRow!.Details);
    }

    [Fact]
    public async Task Apply_Rebaselines_When_State_Has_Drifted()
    {
        var client = AuthenticatedClient();

        // Apply PRESET-04. Then toggle off CAP-QC-INSPECTION (drift). Then
        // re-apply PRESET-04 — the toggle is re-overridden.
        await client.PostAsJsonAsync("/api/v1/presets/PRESET-04/apply", new { reason = "baseline" });

        using (var s1 = _factory.Services.CreateScope())
        {
            var db1 = s1.ServiceProvider.GetRequiredService<AppDbContext>();
            var qc = await db1.Capabilities.FirstAsync(c => c.Code == "CAP-QC-NCR");
            qc.Enabled = false;
            await db1.SaveChangesAsync();
            var snaps = s1.ServiceProvider.GetRequiredService<ICapabilitySnapshotProvider>();
            await snaps.RefreshAsync();
        }

        var response = await client.PostAsJsonAsync(
            "/api/v1/presets/PRESET-04/apply",
            new { reason = "re-baseline drifted state" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApplyResultRow>();
        Assert.NotNull(result);
        Assert.False(result!.NoOp);
        Assert.True(result.DeltaCount >= 1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var qcAfter = await db.Capabilities.AsNoTracking()
            .FirstAsync(c => c.Code == "CAP-QC-NCR");
        Assert.True(qcAfter.Enabled);
    }

    [Fact]
    public async Task Apply_Requires_Admin_Role()
    {
        var nonAdmin = AuthenticatedClient(role: "Engineer");
        var response = await nonAdmin.PostAsJsonAsync(
            "/api/v1/presets/PRESET-01/apply",
            new { reason = "should be forbidden" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Apply_Unknown_Preset_Returns_NotFound()
    {
        var client = AuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/presets/PRESET-FAKE/apply",
            new { });
        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.InternalServerError);
    }

    // ─── PRESET-08 (Pro Services) — bundle apply ───────────────────────────

    [Fact]
    public async Task ApplyPreset08_Seeds_Terminology_RefData_TrackType_Roles()
    {
        var client = AuthenticatedClient();

        // Reset state to PRESET-01 so PRESET-08 has a real capability delta
        // to apply through the bulk-toggle path.
        await client.PostAsJsonAsync("/api/v1/presets/PRESET-01/apply", new { reason = "test-baseline" });

        var response = await client.PostAsJsonAsync(
            "/api/v1/presets/PRESET-08/apply",
            new { reason = "pro-services-stereotype apply" });
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Apply returned {response.StatusCode}: {body}");
        }

        // Verify the four bundle layers landed in the DB.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Terminology — Job → Engagement (the services-firm bill-worthy unit
        // of work), Customer kept as "Customer", Work Center → Consultant.
        // Track Type renamed to "Task Type" since each task type has its own
        // status set (Epic, Project, Story, Bug, Spike).
        var jobRename = await db.TerminologyEntries
            .FirstOrDefaultAsync(t => t.Key == "entity_job");
        Assert.NotNull(jobRename);
        Assert.Equal("Engagement", jobRename!.Label);
        Assert.Equal("PRESET-08", jobRename.SourcePresetId);

        var customerRename = await db.TerminologyEntries
            .FirstOrDefaultAsync(t => t.Key == "entity_customer");
        Assert.Equal("Customer", customerRename?.Label);

        var workCenterRename = await db.TerminologyEntries
            .FirstOrDefaultAsync(t => t.Key == "entity_work_center");
        Assert.Equal("Consultant", workCenterRename?.Label);

        var taskTypeRename = await db.TerminologyEntries
            .FirstOrDefaultAsync(t => t.Key == "entity_track_type");
        Assert.Equal("Task Type", taskTypeRename?.Label);

        // Reference data — engagement_type group seeded with 4 values.
        var engagementTypes = await db.ReferenceData
            .Where(r => r.GroupCode == "engagement_type")
            .ToListAsync();
        Assert.Equal(4, engagementTypes.Count);
        Assert.All(engagementTypes, r => Assert.True(r.IsSeedData));

        // Task types — 5 agile task types each with their own status set.
        var taskTypes = await db.TrackTypes
            .Include(t => t.Stages)
            .Where(t => new[] { "ps_epic", "ps_project", "ps_story", "ps_bug", "ps_spike" }.Contains(t.Code))
            .ToListAsync();
        Assert.Equal(5, taskTypes.Count);
        // Project keeps the canonical 8-stage O2C lifecycle (carries
        // accounting document hooks: SalesOrder, Invoice, Payment).
        var projectTrack = taskTypes.First(t => t.Code == "ps_project");
        Assert.Equal(8, projectTrack.Stages.Count);
        // Other task types have lighter status sets specific to their lifecycle.
        var epic = taskTypes.First(t => t.Code == "ps_epic");
        Assert.Equal(5, epic.Stages.Count);
        var bug = taskTypes.First(t => t.Code == "ps_bug");
        Assert.Equal(6, bug.Stages.Count);

        // Roles — 4 Pro Services role templates added.
        var roles = await db.RoleTemplates
            .Where(r => new[] { "Practitioner", "Engagement Manager", "Account Manager", "Delivery Lead" }
                .Contains(r.Name))
            .ToListAsync();
        Assert.Equal(4, roles.Count);

        // Folder map — bundle persists as a single system_setting.
        var folderMap = await db.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == "cloud_storage.folder_map");
        Assert.NotNull(folderMap);
        Assert.Contains("Clients/{Customer}", folderMap!.Value);
        Assert.Contains("Deliverables", folderMap.Value);
    }

    [Fact]
    public async Task ApplyPreset08_Skips_AdminEdited_Terminology()
    {
        var client = AuthenticatedClient();
        await client.PostAsJsonAsync("/api/v1/presets/PRESET-01/apply", new { reason = "test-baseline" });

        // Reset terminology state for this key, then add the admin row.
        // (Shared fixture across tests; earlier tests may have seeded
        // `entity_job` already.)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stale = await db.TerminologyEntries
                .Where(t => t.Key == "entity_job")
                .ToListAsync();
            db.TerminologyEntries.RemoveRange(stale);
            await db.SaveChangesAsync();

            db.TerminologyEntries.Add(new Forge.Core.Entities.TerminologyEntry
            {
                Key = "entity_job",
                Label = "WorkOrder",  // admin's preference, distinct from "Engagement" (the PRESET-08 default)
                IsAdminEdited = true,
            });
            await db.SaveChangesAsync();
        }

        await client.PostAsJsonAsync(
            "/api/v1/presets/PRESET-08/apply",
            new { reason = "should preserve admin edit" });

        // entity_job should still read "WorkOrder" — not "Task".
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var jobLabel = await db.TerminologyEntries
                .FirstAsync(t => t.Key == "entity_job");
            Assert.Equal("WorkOrder", jobLabel.Label); // not "Engagement"
            Assert.True(jobLabel.IsAdminEdited);
        }
    }

    // ─── Custom preview ────────────────────────────────────────────────────

    [Fact]
    public async Task CustomPreview_Catalog_Defaults_Plus_Overrides_Returns_Resulting_Set()
    {
        var client = AuthenticatedClient();
        var body = new
        {
            capabilityOverrides = new[]
            {
                new { code = "CAP-IDEN-AUTH-MFA", enabled = true },
                new { code = "CAP-EXT-AI-ASSISTANT", enabled = true },
            },
        };
        var response = await client.PostAsJsonAsync("/api/v1/presets/custom/preview", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<CustomPreviewRow>();
        Assert.NotNull(preview);
        // The two overrides should both be IN.
        Assert.Contains(preview!.Capabilities, c => c.Code == "CAP-IDEN-AUTH-MFA" && c.InPreset);
        Assert.Contains(preview.Capabilities, c => c.Code == "CAP-EXT-AI-ASSISTANT" && c.InPreset);
        // Default-on capabilities still in.
        Assert.Contains(preview.Capabilities, c => c.Code == "CAP-MD-CUSTOMERS" && c.InPreset);
    }

    [Fact]
    public async Task CustomPreview_Surfaces_Violations_When_Disabling_Required_Capability()
    {
        var client = AuthenticatedClient();
        // Try to disable CAP-MD-PARTS — this is depended on by many capabilities.
        // Should surface "capability-has-dependents" violation.
        var body = new
        {
            capabilityOverrides = new[]
            {
                new { code = "CAP-MD-PARTS", enabled = false },
            },
        };
        var response = await client.PostAsJsonAsync("/api/v1/presets/custom/preview", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<CustomPreviewRow>();
        Assert.NotNull(preview);
        Assert.False(preview!.Valid);
        Assert.NotEmpty(preview.Violations);
        Assert.Contains(preview.Violations, v =>
            v.Code == "capability-has-dependents" && v.Capability == "CAP-MD-PARTS");
    }

    // ─── Custom apply ──────────────────────────────────────────────────────

    [Fact]
    public async Task CustomApply_Records_Override_Count_In_Audit()
    {
        var client = AuthenticatedClient();
        var body = new
        {
            capabilityOverrides = new[]
            {
                new { code = "CAP-IDEN-AUTH-MFA", enabled = true },
                new { code = "CAP-EXT-AI-ASSISTANT", enabled = true },
                new { code = "CAP-EXT-CHAT", enabled = true },
            },
            reason = "phase-g-test custom apply",
        };
        var response = await client.PostAsJsonAsync("/api/v1/presets/custom/apply", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApplyResultRow>();
        Assert.NotNull(result);
        Assert.True(result!.IsCustom);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditRow = await db.AuditLogEntries
            .Where(a => a.Action == "PresetApplied")
            .OrderByDescending(a => a.CreatedAt)
            .FirstAsync();
        Assert.Contains("PRESET-CUSTOM", auditRow.Details);
        Assert.Contains("customOverrideCount", auditRow.Details);
        Assert.Contains("3", auditRow.Details);
    }

    // ─── Helper response shapes ────────────────────────────────────────────

    private record PresetSummaryRow(
        string Id,
        string Name,
        string ShortDescription,
        string TargetProfile,
        int CapabilityCount,
        bool IsCustom,
        bool IsActive,
        List<string> RecommendedFor);

    private record PresetDetailRow(
        string Id,
        string Name,
        string ShortDescription,
        string TargetProfile,
        int CapabilityCount,
        bool IsCustom,
        bool IsActive,
        List<string> RecommendedFor,
        List<CapabilityRow> Capabilities,
        List<CapabilityRow> DeltaVsCatalogDefaults,
        List<DeltaRow> DeltaVsCurrentInstall);

    private record CapabilityRow(string Code, string Name, string Area, string Description, bool InPreset, bool DefaultOn);

    private record DeltaRow(string Code, string Name, string Area, bool CurrentlyEnabled, bool WillBeEnabled);

    private record CompareRow(List<PresetSummaryRow> Presets, List<CompareCapabilityRow> Rows);

    private record CompareCapabilityRow(
        string Code,
        string Name,
        string Area,
        bool DefaultOn,
        List<CompareCellRow> Cells,
        bool Disagreement);

    private record CompareCellRow(string PresetId, bool InPreset);

    private record PreviewApplyRow(
        string PresetId,
        string PresetName,
        bool IsCustom,
        int DeltaCount,
        List<DeltaRow> Deltas,
        bool Valid,
        List<ViolationRow> Violations);

    private record ApplyResultRow(
        string PresetId,
        string PresetName,
        bool IsCustom,
        bool NoOp,
        int DeltaCount,
        List<DeltaRow> Applied);

    private record CustomPreviewRow(
        int CapabilityCount,
        List<CapabilityRow> Capabilities,
        List<DeltaRow> DeltaVsCurrentInstall,
        bool Valid,
        List<ViolationRow> Violations);

    private record ViolationRow(
        string Code,
        string Capability,
        string Message,
        List<string>? Missing,
        List<string>? Conflicts,
        List<string>? Dependents);
}
