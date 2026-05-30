using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts.PurchaseOptions;

/// <summary>Shared read projection so create/update return the same shape as the list query.</summary>
internal static class PartPurchaseOptionProjection
{
    public static async Task<PartPurchaseOptionResponseModel> SingleAsync(AppDbContext db, int id, CancellationToken ct)
        => await db.PartPurchaseOptions
            .AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new PartPurchaseOptionResponseModel(
                o.Id,
                o.PartId,
                o.Label,
                o.ContentQuantity,
                o.ContentUomId,
                o.ContentUom != null ? o.ContentUom.Code : null,
                o.ContentUom != null ? o.ContentUom.Name : null,
                o.SortOrder,
                o.IsActive))
            .FirstAsync(ct);
}
