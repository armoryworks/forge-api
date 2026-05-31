using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Workflows;

/// <summary>
/// Workflow Pattern — Loads a Vendor entity for the predicate evaluator.
/// Vendor's readiness gates (<c>hasIdentity</c>, <c>hasAddress</c>,
/// <c>hasTerms</c>) all introspect scalar columns on the row itself —
/// no relations need to be eagerly loaded. Mirrors
/// <see cref="PartReadinessLoader"/> for the Vendor entity type.
/// </summary>
public class VendorReadinessLoader(AppDbContext db) : IEntityReadinessLoader
{
    public string EntityType => "Vendor";

    public async Task<object?> LoadAsync(int entityId, CancellationToken ct)
    {
        return await db.Vendors
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == entityId, ct);
    }
}
