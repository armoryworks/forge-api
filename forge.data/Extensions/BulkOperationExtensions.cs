using EFCore.BulkExtensions;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;

namespace Forge.Data.Extensions;

public static class BulkOperationExtensions
{
    public static async Task BulkSoftDeleteAsync<T>(this DbContext context, IList<T> entities, string deletedBy) where T : BaseAuditableEntity
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entity in entities)
        {
            entity.DeletedAt = now;
            entity.DeletedBy = deletedBy;
        }
        await context.BulkUpdateAsync(entities);
    }
}
