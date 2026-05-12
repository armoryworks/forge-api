using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities.Discovery.Bundles;
using Forge.Api.Features.Presets.Apply.Layers;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Capabilities;

/// <summary>
/// Pro Services rollout (Artifact 5 §4) — unit tests for the per-layer
/// bundle appliers. Each applier is exercised directly against an
/// in-memory <see cref="TestAppDbContext"/>: the goal is to verify the
/// per-policy conflict semantics independently of the wider apply pipeline.
/// </summary>
public class PresetBundleApplierTests
{
    private const string TestPresetId = "PRESET-TEST";

    // ─── TerminologyBundleApplier ──────────────────────────────────────────

    [Fact]
    public async Task Terminology_Adds_New_Keys_When_Table_Empty()
    {
        using var db = TestDbContextFactory.Create();
        var bundle = new TerminologyBundle(new Dictionary<string, string>
        {
            ["entity_job"] = "Engagement",
            ["entity_part"] = "Service Item",
        });

        var result = await TerminologyBundleApplier.ApplyAsync(bundle, db, TestPresetId, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(2, result.AddedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(0, result.SkippedCount);

        var rows = await db.TerminologyEntries.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(TestPresetId, r.SourcePresetId));
        Assert.All(rows, r => Assert.False(r.IsAdminEdited));
    }

    [Fact]
    public async Task Terminology_SkipAdminEdited_Leaves_Edited_Rows_Alone()
    {
        using var db = TestDbContextFactory.Create();
        db.TerminologyEntries.Add(new TerminologyEntry
        {
            Key = "entity_job",
            Label = "WorkOrder",
            IsAdminEdited = true,
            SourcePresetId = null,
        });
        await db.SaveChangesAsync();

        var bundle = new TerminologyBundle(
            new Dictionary<string, string> { ["entity_job"] = "Engagement" },
            ConflictPolicy: TerminologyConflictPolicy.SkipAdminEdited);

        var result = await TerminologyBundleApplier.ApplyAsync(bundle, db, TestPresetId, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(0, result.AddedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(1, result.SkippedCount);

        var row = await db.TerminologyEntries.FirstAsync();
        Assert.Equal("WorkOrder", row.Label);
        Assert.True(row.IsAdminEdited);
    }

    [Fact]
    public async Task Terminology_Overwrite_Rewrites_Even_Edited_Rows()
    {
        using var db = TestDbContextFactory.Create();
        db.TerminologyEntries.Add(new TerminologyEntry
        {
            Key = "entity_job",
            Label = "WorkOrder",
            IsAdminEdited = true,
        });
        await db.SaveChangesAsync();

        var bundle = new TerminologyBundle(
            new Dictionary<string, string> { ["entity_job"] = "Engagement" },
            ConflictPolicy: TerminologyConflictPolicy.Overwrite);

        var result = await TerminologyBundleApplier.ApplyAsync(bundle, db, TestPresetId, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(0, result.AddedCount);
        Assert.Equal(1, result.UpdatedCount);

        var row = await db.TerminologyEntries.FirstAsync();
        Assert.Equal("Engagement", row.Label);
        Assert.Equal(TestPresetId, row.SourcePresetId);
    }

    [Fact]
    public async Task Terminology_Prompt_Counts_Conflicted_Rows()
    {
        using var db = TestDbContextFactory.Create();
        db.TerminologyEntries.Add(new TerminologyEntry
        {
            Key = "entity_job",
            Label = "WorkOrder",
            IsAdminEdited = true,
        });
        await db.SaveChangesAsync();

        var bundle = new TerminologyBundle(
            new Dictionary<string, string> { ["entity_job"] = "Engagement" },
            ConflictPolicy: TerminologyConflictPolicy.Prompt);

        var result = await TerminologyBundleApplier.ApplyAsync(bundle, db, TestPresetId, CancellationToken.None);

        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(1, result.ConflictedCount);
    }

    // ─── ReferenceDataBundleApplier ────────────────────────────────────────

    [Fact]
    public async Task ReferenceData_UpsertSeed_Adds_Missing_Leaves_Existing()
    {
        using var db = TestDbContextFactory.Create();
        db.ReferenceData.Add(new ReferenceData
        {
            GroupCode = "engagement_type",
            Code = "consulting",
            Label = "Custom Consulting Label",  // admin-edited value
            SortOrder = 1,
            IsSeedData = false,
        });
        await db.SaveChangesAsync();

        var seeds = new Dictionary<string, IReadOnlyList<ReferenceDataValueSeed>>
        {
            ["engagement_type"] = new List<ReferenceDataValueSeed>
            {
                new("consulting", "Consulting", 1),
                new("project",    "Project",    2),
                new("retainer",   "Retainer",   3),
            },
        };
        var bundle = new ReferenceDataBundle(seeds);

        var result = await ReferenceDataBundleApplier.ApplyAsync(bundle, db, TestPresetId, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(2, result.AddedCount);     // project + retainer
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(1, result.SkippedCount);    // consulting already exists

        var existing = await db.ReferenceData.FirstAsync(r => r.Code == "consulting");
        Assert.Equal("Custom Consulting Label", existing.Label);  // unchanged
    }

    [Fact]
    public async Task ReferenceData_Overwrite_Updates_Existing_Values()
    {
        using var db = TestDbContextFactory.Create();
        db.ReferenceData.Add(new ReferenceData
        {
            GroupCode = "engagement_type",
            Code = "consulting",
            Label = "Old Label",
            SortOrder = 9,
        });
        await db.SaveChangesAsync();

        var bundle = new ReferenceDataBundle(
            new Dictionary<string, IReadOnlyList<ReferenceDataValueSeed>>
            {
                ["engagement_type"] = new List<ReferenceDataValueSeed>
                {
                    new("consulting", "Consulting", 1),
                },
            },
            ConflictPolicy: ReferenceDataConflictPolicy.Overwrite);

        var result = await ReferenceDataBundleApplier.ApplyAsync(bundle, db, TestPresetId, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(1, result.UpdatedCount);
        var row = await db.ReferenceData.FirstAsync();
        Assert.Equal("Consulting", row.Label);
        Assert.Equal(1, row.SortOrder);
    }

    [Fact]
    public async Task ReferenceData_Skip_Skips_Whole_Group_When_Existing_Rows()
    {
        using var db = TestDbContextFactory.Create();
        db.ReferenceData.Add(new ReferenceData
        {
            GroupCode = "engagement_type",
            Code = "consulting",
            Label = "Consulting",
        });
        await db.SaveChangesAsync();

        var bundle = new ReferenceDataBundle(
            new Dictionary<string, IReadOnlyList<ReferenceDataValueSeed>>
            {
                ["engagement_type"] = new List<ReferenceDataValueSeed>
                {
                    new("consulting", "Consulting", 1),
                    new("project",    "Project",    2),
                    new("retainer",   "Retainer",   3),
                },
            },
            ConflictPolicy: ReferenceDataConflictPolicy.Skip);

        var result = await ReferenceDataBundleApplier.ApplyAsync(bundle, db, TestPresetId, CancellationToken.None);

        Assert.Equal(0, result.AddedCount);
        Assert.Equal(3, result.SkippedCount);  // whole group skipped
    }

    // ─── TrackTypeBundleApplier ────────────────────────────────────────────

    [Fact]
    public async Task TrackType_UpsertByCode_Adds_New_TrackType_With_Stages()
    {
        using var db = TestDbContextFactory.Create();
        var bundle = new TrackTypeBundle(new[]
        {
            new TrackTypeSeed(
                Code: "engagement",
                Name: "Engagement",
                SortOrder: 1,
                IsDefault: false,
                IsShopFloor: false,
                Stages: new[]
                {
                    new JobStageSeed("proposal", "Proposal", 1, "#94a3b8"),
                    new JobStageSeed("active",   "Active",   2, "#f59e0b"),
                }),
        });

        var result = await TrackTypeBundleApplier.ApplyAsync(bundle, db, TestPresetId, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(1, result.AddedCount);
        var track = await db.TrackTypes.Include(t => t.Stages).FirstAsync();
        Assert.Equal("engagement", track.Code);
        Assert.Equal(2, track.Stages.Count);
    }

    [Fact]
    public async Task TrackType_UpsertByCode_Adds_Missing_Stages_To_Existing_Track()
    {
        using var db = TestDbContextFactory.Create();
        var existing = new TrackType { Code = "engagement", Name = "Engagement", SortOrder = 1 };
        existing.Stages.Add(new JobStage { Code = "proposal", Name = "Proposal", SortOrder = 1 });
        db.TrackTypes.Add(existing);
        await db.SaveChangesAsync();

        var bundle = new TrackTypeBundle(new[]
        {
            new TrackTypeSeed(
                Code: "engagement",
                Name: "Engagement",
                SortOrder: 1,
                IsDefault: false,
                IsShopFloor: false,
                Stages: new[]
                {
                    new JobStageSeed("proposal", "Proposal", 1, "#94a3b8"),   // skip - exists
                    new JobStageSeed("active",   "Active",   2, "#f59e0b"),   // add
                }),
        });

        var result = await TrackTypeBundleApplier.ApplyAsync(bundle, db, TestPresetId, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(1, result.UpdatedCount);  // track touched (new stage added)
        var stages = await db.JobStages.Where(s => s.TrackTypeId == existing.Id).ToListAsync();
        Assert.Equal(2, stages.Count);
        Assert.Contains(stages, s => s.Code == "active");
    }

    [Fact]
    public async Task TrackType_AddOnly_Skips_Existing_Track()
    {
        using var db = TestDbContextFactory.Create();
        db.TrackTypes.Add(new TrackType { Code = "engagement", Name = "Engagement", SortOrder = 1 });
        await db.SaveChangesAsync();

        var bundle = new TrackTypeBundle(
            new[]
            {
                new TrackTypeSeed("engagement", "Engagement", 1, false, false,
                    new[] { new JobStageSeed("new-stage", "New Stage", 1) }),
            },
            ConflictPolicy: TrackTypeConflictPolicy.AddOnly);

        var result = await TrackTypeBundleApplier.ApplyAsync(bundle, db, TestPresetId, CancellationToken.None);

        Assert.Equal(0, result.AddedCount);
        Assert.Equal(1, result.SkippedCount);
    }

    // ─── RoleBundleApplier ─────────────────────────────────────────────────

    [Fact]
    public async Task Role_AddOnly_Inserts_Missing_And_Skips_Existing()
    {
        using var db = TestDbContextFactory.Create();
        db.RoleTemplates.Add(new RoleTemplate
        {
            Name = "Engagement Manager",
            Description = "Existing description",
            IsSystemDefault = false,
            IncludedRoleNamesJson = "[\"Manager\"]",
        });
        await db.SaveChangesAsync();

        var bundle = new RoleBundle(new[]
        {
            new RoleSeed("Engagement Manager", "Engagement Manager",
                Description: "Bundle description",
                DefaultPermissions: new[] { "Manager" }),
            new RoleSeed("Practitioner", "Practitioner",
                Description: "Service practitioner",
                DefaultPermissions: new[] { "Engineer" }),
        });

        var result = await RoleBundleApplier.ApplyAsync(bundle, db, TestPresetId, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(1, result.AddedCount);
        Assert.Equal(1, result.SkippedCount);

        var existing = await db.RoleTemplates.FirstAsync(r => r.Name == "Engagement Manager");
        Assert.Equal("Existing description", existing.Description);
    }

    [Fact]
    public async Task Role_UpsertByCode_Updates_Existing_When_Different()
    {
        using var db = TestDbContextFactory.Create();
        db.RoleTemplates.Add(new RoleTemplate
        {
            Name = "Engagement Manager",
            Description = "Old description",
            IncludedRoleNamesJson = "[\"OldRole\"]",
        });
        await db.SaveChangesAsync();

        var bundle = new RoleBundle(
            new[] { new RoleSeed("Engagement Manager", "Engagement Manager",
                Description: "New description",
                DefaultPermissions: new[] { "Manager" }) },
            ConflictPolicy: RoleConflictPolicy.UpsertByCode);

        var result = await RoleBundleApplier.ApplyAsync(bundle, db, TestPresetId, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(1, result.UpdatedCount);
        var row = await db.RoleTemplates.FirstAsync();
        Assert.Equal("New description", row.Description);
        Assert.Contains("Manager", row.IncludedRoleNamesJson);
    }

    // ─── WorkflowDefinitionBundleApplier ───────────────────────────────────

    [Fact]
    public async Task WorkflowDefinition_Adds_New_Definition()
    {
        using var db = TestDbContextFactory.Create();
        var bundle = new WorkflowDefinitionBundle(
            new Dictionary<string, string>
            {
                ["Job"] = "{\"steps\":[]}",
            });

        var result = await WorkflowDefinitionBundleApplier.ApplyAsync(bundle, db, TestPresetId, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(1, result.AddedCount);
        var row = await db.WorkflowDefinitions.FirstAsync();
        Assert.Equal("Job", row.EntityType);
        Assert.True(row.IsSeedData);
        Assert.Equal("job-preset-test-v1", row.DefinitionId);
    }

    [Fact]
    public async Task WorkflowDefinition_Updates_Seed_Row_With_New_Steps()
    {
        using var db = TestDbContextFactory.Create();
        db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            DefinitionId = "job-preset-test-v1",
            EntityType = "Job",
            StepsJson = "{\"steps\":[\"old\"]}",
            IsSeedData = true,
        });
        await db.SaveChangesAsync();

        var bundle = new WorkflowDefinitionBundle(
            new Dictionary<string, string>
            {
                ["Job"] = "{\"steps\":[\"new\"]}",
            });

        var result = await WorkflowDefinitionBundleApplier.ApplyAsync(bundle, db, TestPresetId, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(1, result.UpdatedCount);
        var row = await db.WorkflowDefinitions.FirstAsync();
        Assert.Contains("new", row.StepsJson);
    }

    // ─── PRESET-04 bundle re-apply against existing seed (Wave 12) ─────────

    [Fact]
    public async Task TrackType_UpsertByCode_Reapplied_Against_Existing_Seed_Adds_Missing_Stages()
    {
        using var db = TestDbContextFactory.Create();
        // Simulate what SeedData.Essential already wrote — the Production
        // track exists with a partial set of stages already.
        var existing = new TrackType { Code = "production", Name = "Production", SortOrder = 1, IsDefault = true, IsShopFloor = true };
        foreach (var (code, name, sortOrder) in new (string, string, int)[]
        {
            ("quote_requested", "Quote Requested", 1),
            ("quoted", "Quoted", 2),
            ("order_confirmed", "Order Confirmed", 3),
            ("materials_ordered", "Materials Ordered", 4),
        })
        {
            existing.Stages.Add(new JobStage { Code = code, Name = name, SortOrder = sortOrder });
        }
        db.TrackTypes.Add(existing);
        await db.SaveChangesAsync();

        // Re-apply PRESET-04's bundle (canonical preset record).
        var preset = Forge.Api.Capabilities.Discovery.PresetCatalog.Preset04_ProductionManufacturer;
        Assert.NotNull(preset.TrackTypeBundle);

        await TrackTypeBundleApplier.ApplyAsync(
            preset.TrackTypeBundle!, db, preset.Id, CancellationToken.None);
        await db.SaveChangesAsync();

        // No duplicate Production track.
        var prodTracks = await db.TrackTypes
            .Where(t => t.Code == "production")
            .ToListAsync();
        Assert.Single(prodTracks);

        // All 10 Production stages now present (4 pre-existing + 6 new).
        var prodStages = await db.JobStages
            .Where(s => s.TrackTypeId == existing.Id)
            .ToListAsync();
        Assert.Equal(10, prodStages.Count);

        // R&D + Maintenance tracks added (they didn't pre-exist).
        var allTracks = await db.TrackTypes.Select(t => t.Code).ToListAsync();
        Assert.Contains("rnd", allTracks);
        Assert.Contains("maintenance", allTracks);
    }

    // ─── FolderMapBundleApplier ────────────────────────────────────────────

    [Fact]
    public async Task FolderMap_First_Apply_Inserts_System_Setting()
    {
        using var db = TestDbContextFactory.Create();
        var bundle = new FolderMapBundle(new[]
        {
            new FolderMapSuggestion("Customer", "/Clients/{Customer}/", new[] { "Contracts" }),
            new FolderMapSuggestion("Job",      "/Clients/{Customer}/{Job}/", new[] { "Working", "Final" }),
        });

        var result = await FolderMapBundleApplier.ApplyAsync(bundle, db, TestPresetId, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(2, result.AddedCount);
        var setting = await db.SystemSettings.FirstAsync(s => s.Key == FolderMapBundleApplier.FolderMapSettingKey);
        Assert.Contains("Clients/{Customer}", setting.Value);
    }

    [Fact]
    public async Task FolderMap_Re_Apply_With_Identical_Bundle_Is_Noop()
    {
        using var db = TestDbContextFactory.Create();
        var bundle = new FolderMapBundle(new[]
        {
            new FolderMapSuggestion("Customer", "/Clients/{Customer}/", new[] { "Contracts" }),
        });

        await FolderMapBundleApplier.ApplyAsync(bundle, db, TestPresetId, CancellationToken.None);
        await db.SaveChangesAsync();
        var second = await FolderMapBundleApplier.ApplyAsync(bundle, db, TestPresetId, CancellationToken.None);

        Assert.Equal(0, second.AddedCount);
        Assert.Equal(0, second.UpdatedCount);
        Assert.Equal(1, second.SkippedCount);
    }

    [Fact]
    public async Task FolderMap_Re_Apply_With_Changed_Bundle_Updates_Setting()
    {
        using var db = TestDbContextFactory.Create();
        var first = new FolderMapBundle(new[]
        {
            new FolderMapSuggestion("Customer", "/Customers/{Customer}/", new[] { "Files" }),
        });
        var second = new FolderMapBundle(new[]
        {
            new FolderMapSuggestion("Customer", "/Clients/{Customer}/", new[] { "Contracts", "Engagements" }),
        });

        await FolderMapBundleApplier.ApplyAsync(first, db, TestPresetId, CancellationToken.None);
        await db.SaveChangesAsync();
        var result = await FolderMapBundleApplier.ApplyAsync(second, db, TestPresetId, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(0, result.AddedCount);
        Assert.Equal(1, result.UpdatedCount);

        var setting = await db.SystemSettings.FirstAsync(s => s.Key == FolderMapBundleApplier.FolderMapSettingKey);
        Assert.Contains("Clients/{Customer}", setting.Value);
        Assert.Contains("Engagements", setting.Value);
        Assert.DoesNotContain("Customers/{Customer}", setting.Value);
    }

    // ─── WorkflowDefinitionBundleApplier ───────────────────────────────────

    [Fact]
    public async Task WorkflowDefinition_Skips_Admin_Edited_Row()
    {
        using var db = TestDbContextFactory.Create();
        db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            DefinitionId = "job-preset-test-v1",
            EntityType = "Job",
            StepsJson = "{\"steps\":[\"admin\"]}",
            IsSeedData = false,   // admin edited
        });
        await db.SaveChangesAsync();

        var bundle = new WorkflowDefinitionBundle(
            new Dictionary<string, string> { ["Job"] = "{\"steps\":[\"new\"]}" });

        var result = await WorkflowDefinitionBundleApplier.ApplyAsync(bundle, db, TestPresetId, CancellationToken.None);

        Assert.Equal(1, result.SkippedCount);
        var row = await db.WorkflowDefinitions.FirstAsync();
        Assert.Contains("admin", row.StepsJson);
    }
}
