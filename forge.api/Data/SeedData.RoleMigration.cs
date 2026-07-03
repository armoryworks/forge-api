using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Data;

public static partial class SeedData
{
    /// <summary>
    /// One-time, idempotent migration off the user-side RoleTemplate concept (retired in
    /// favour of direct multi-role assignment). For every user that still carries a
    /// <c>role_template_id</c>, the template's <c>included_role_names_json</c> roles are
    /// added directly to the user (<c>asp_net_user_roles</c>) and the FK is cleared.
    /// <para>
    /// Deliberately raw SQL, not EF: the <c>ApplicationUser.RoleTemplateId</c> property has
    /// been removed from the entity, so it can no longer be queried through the model. The
    /// step is guarded on the column still existing (it is dropped from the declarative
    /// schema in the same release, applied out-of-band by pg-schema-diff at deploy time —
    /// this boot migration must have run at least once while the column still held data).
    /// Once the column is gone the guard makes this a fast no-op.
    /// </para>
    /// <para>
    /// Note: this migrates only the <b>user</b> template coupling. <c>role_templates</c>
    /// itself is retained — it independently backs SystemApiKey role-scoping.
    /// </para>
    /// </summary>
    public static async Task MigrateUserRoleTemplatesToDirectRolesAsync(AppDbContext db, CancellationToken ct = default)
    {
        // Guard: skip entirely once the column has been dropped from asp_net_users.
        var columnExists = await db.Database
            .SqlQuery<bool>($@"SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'asp_net_users'
                  AND column_name = 'role_template_id') AS ""Value""")
            .SingleAsync(ct);

        if (!columnExists)
            return;

        // Expand each assigned template into direct role rows (idempotent via ON CONFLICT),
        // then clear the FK so the same user is never re-expanded.
        var expanded = await db.Database.ExecuteSqlRawAsync(@"
            INSERT INTO asp_net_user_roles (user_id, role_id)
            SELECT u.id, r.id
            FROM asp_net_users u
            JOIN role_templates t ON t.id = u.role_template_id
            CROSS JOIN LATERAL jsonb_array_elements_text(t.included_role_names_json::jsonb) AS names(name)
            JOIN asp_net_roles r ON r.name = names.name
            WHERE u.role_template_id IS NOT NULL
            ON CONFLICT (user_id, role_id) DO NOTHING;", ct);

        var cleared = await db.Database.ExecuteSqlRawAsync(
            "UPDATE asp_net_users SET role_template_id = NULL WHERE role_template_id IS NOT NULL;", ct);

        if (cleared > 0)
            Log.Information(
                "Migrated {Cleared} user(s) off role templates to direct roles ({Expanded} role grants added)",
                cleared, expanded);
    }
}
