using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Data.Extensions;

/// <summary>
/// Helpers for writing rows into <c>activity_logs</c> from MediatR handlers.
///
/// <para><strong>Indexing-points rule (project-wide).</strong> When a mutation
/// touches an entity that sits at an indexing point between multiple tracked
/// entities (e.g. <c>VendorPart</c> bridges Part ↔ Vendor; a BOM line bridges
/// parent Part ↔ component Part; a sales-order line bridges SalesOrder ↔
/// Part; etc.), the activity row MUST be written for every involved entity —
/// not just the one the user is currently viewing. The helper below makes
/// that one call: pass every (EntityType, EntityId) pair the change is
/// relevant to, and the helper inserts one row per pair.</para>
///
/// <para><strong>Rollup rule.</strong> A multi-field update is a single
/// activity row whose Description summarizes all changes (e.g. "Updated 4
/// fields: leadTimeDays, minOrderQty, packSize, notes"). Per-field history
/// rows belong on the History tab, which is a different stream — do not
/// emit one ActivityLog per field. See CLAUDE.md "Activity logging" for the
/// authoritative spec.</para>
/// </summary>
public static class ActivityLogExtensions
{
    /// <summary>
    /// Adds one <see cref="ActivityLog"/> row per indexing point, all sharing
    /// the same action / description / user. The caller is still responsible
    /// for <see cref="AppDbContext.SaveChangesAsync(System.Threading.CancellationToken)"/>.
    /// </summary>
    /// <param name="db">DbContext (used for <c>CurrentUserId</c> and <c>ActivityLogs</c>).</param>
    /// <param name="action">Short kebab-or-snake-case verb. Convention: <c>created</c>, <c>updated</c>, <c>deleted</c>, plus domain verbs (<c>preferred-vendor-changed</c>, <c>price-tier-added</c>, etc.).</param>
    /// <param name="description">One-line, human-readable summary. For multi-field updates, list the changed fields.</param>
    /// <param name="indexingPoints">Every entity (type + id) the change should appear under. Order doesn't matter.</param>
    public static void LogActivityAt(
        this AppDbContext db,
        string action,
        string description,
        params (string EntityType, int EntityId)[] indexingPoints)
    {
        var userId = db.CurrentUserId;
        foreach (var (entityType, entityId) in indexingPoints)
        {
            db.ActivityLogs.Add(new ActivityLog
            {
                EntityType = entityType,
                EntityId = entityId,
                UserId = userId,
                Action = action,
                Description = description,
            });
        }
    }
}
