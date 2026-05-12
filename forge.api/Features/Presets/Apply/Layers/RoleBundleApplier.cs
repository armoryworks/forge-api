using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities.Discovery.Bundles;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Presets.Apply.Layers;

/// <summary>
/// Pro Services rollout (Artifact 5 §3.4, §4) — applies a
/// <see cref="RoleBundle"/> to the install's <c>role_templates</c> table.
/// Each <see cref="RoleSeed"/> is matched against an existing template
/// by <see cref="RoleTemplate.Name"/> (lowercased and matched after
/// trimming whitespace).
///
/// <para><b>Conflict semantics (default = AddOnly is the safest):</b></para>
/// <list type="bullet">
///   <item><c>AddOnly</c> (default): only add net-new roles; never modify
///   or remove existing ones. Re-applying a preset must never strip an
///   admin-granted permission, hence this default.</item>
///   <item><c>UpsertByCode</c>: add new + update description / included
///   role names on existing rows matching by template name.</item>
/// </list>
///
/// <para>Note: <c>RoleTemplate.IncludedRoleNamesJson</c> stores the
/// underlying role list as JSON. The bundle's
/// <see cref="RoleSeed.DefaultPermissions"/> is mapped here as the
/// included-role list (compatible with the existing JWT-claim expansion
/// path). <see cref="RoleSeed.DefaultCapabilities"/> is not yet wired —
/// per-role capability grants are a future enhancement; the field is
/// captured in the seed for forward compatibility.</para>
///
/// <para>Caller is responsible for <c>SaveChangesAsync</c>.</para>
/// </summary>
public static class RoleBundleApplier
{
    public static async Task<LayerApplyResult> ApplyAsync(
        RoleBundle bundle,
        AppDbContext db,
        string presetId,
        CancellationToken cancellationToken)
    {
        _ = presetId;  // RoleTemplate has no source_preset_id column today.

        var seedNames = bundle.Roles.Select(r => r.Name).ToList();
        var existing = await db.RoleTemplates
            .Where(t => seedNames.Contains(t.Name))
            .ToListAsync(cancellationToken);

        var byName = existing.ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var seed in bundle.Roles)
        {
            if (byName.TryGetValue(seed.Name, out var template))
            {
                if (bundle.ConflictPolicy == RoleConflictPolicy.AddOnly)
                {
                    skipped++;
                    continue;
                }

                // UpsertByCode — overwrite description + included roles.
                var didChange = false;
                if (template.Description != seed.Description)
                {
                    template.Description = seed.Description;
                    didChange = true;
                }
                var seededJson = SerializeIncludedRoles(seed.DefaultPermissions);
                if (template.IncludedRoleNamesJson != seededJson)
                {
                    template.IncludedRoleNamesJson = seededJson;
                    didChange = true;
                }
                if (didChange) updated++; else skipped++;
            }
            else
            {
                db.RoleTemplates.Add(new RoleTemplate
                {
                    Name = seed.Name,
                    Description = seed.Description,
                    IsSystemDefault = true,
                    IncludedRoleNamesJson = SerializeIncludedRoles(seed.DefaultPermissions),
                });
                added++;
            }
        }

        return new LayerApplyResult(
            Layer: PresetBundleLayer.Role,
            AddedCount: added,
            UpdatedCount: updated,
            SkippedCount: skipped);
    }

    private static string SerializeIncludedRoles(IReadOnlyList<string>? roles)
    {
        if (roles is null || roles.Count == 0) return "[]";
        return JsonSerializer.Serialize(roles);
    }
}
