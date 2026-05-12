using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Workflows;

/// <summary>
/// Workflow Pattern Phase 3 — Loads a Part entity with the relations the
/// readiness predicates need to introspect: BOM entries (hasBom),
/// operations (hasRouting). Cost columns live on the Part row itself so
/// no extra Include is required for hasCost.
/// </summary>
public class PartReadinessLoader(AppDbContext db) : IEntityReadinessLoader
{
    public string EntityType => "Part";

    public async Task<object?> LoadAsync(int entityId, CancellationToken ct)
    {
        return await db.Parts
            .AsNoTracking()
            .Include(p => p.BOMEntries)
            .Include(p => p.Operations)
            .FirstOrDefaultAsync(p => p.Id == entityId, ct);
    }
}
