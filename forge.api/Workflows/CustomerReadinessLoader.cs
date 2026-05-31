using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Workflows;

/// <summary>
/// Workflow Pattern — Loads a Customer for predicate evaluation. The
/// hasIdentity gate inspects the <c>name</c> scalar column on the row, so
/// no relations need to be eagerly loaded today. Mirrors
/// <see cref="VendorReadinessLoader"/>.
/// </summary>
public class CustomerReadinessLoader(AppDbContext db) : IEntityReadinessLoader
{
    public string EntityType => "Customer";

    public async Task<object?> LoadAsync(int entityId, CancellationToken ct)
    {
        return await db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == entityId, ct);
    }
}
