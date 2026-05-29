using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Parts.PurchaseOptions;

/// <summary>
/// UoM purchase-options effort — a purchase option's content UoM must be in the same
/// <c>UomCategory</c> as the part's stock UoM (area↔area, mass↔mass…). Otherwise the
/// cost derivation (tier price ÷ content) would mix dimensions ("8 grams" for an area part).
/// No-op when either UoM is unset.
/// </summary>
internal static class PurchaseOptionUomGuard
{
    public static async Task EnsureCompatibleAsync(AppDbContext db, int partId, int? contentUomId, CancellationToken ct)
    {
        if (!contentUomId.HasValue)
            return;

        var stockUomId = await db.Parts.AsNoTracking()
            .Where(p => p.Id == partId)
            .Select(p => p.StockUomId)
            .FirstOrDefaultAsync(ct);

        if (!stockUomId.HasValue)
            return;

        var cats = await db.UnitsOfMeasure.AsNoTracking()
            .Where(u => u.Id == contentUomId.Value || u.Id == stockUomId.Value)
            .Select(u => new { u.Id, u.Category })
            .ToListAsync(ct);

        var contentCat = cats.FirstOrDefault(c => c.Id == contentUomId.Value)?.Category;
        var stockCat = cats.FirstOrDefault(c => c.Id == stockUomId.Value)?.Category;

        if (contentCat.HasValue && stockCat.HasValue && contentCat != stockCat)
            throw new InvalidOperationException(
                "The purchase option's content UoM must be in the same category as the part's stock UoM.");
    }
}
