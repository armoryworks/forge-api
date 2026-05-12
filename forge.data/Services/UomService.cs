using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Data.Services;

public class UomService(AppDbContext db) : IUomService
{
    public async Task<decimal> ConvertAsync(decimal quantity, int fromUomId, int toUomId, int? partId, CancellationToken ct)
    {
        if (fromUomId == toUomId) return quantity;

        var result = await TryConvertAsync(quantity, fromUomId, toUomId, partId, ct);
        return result ?? throw new InvalidOperationException(
            $"No conversion path found from UOM {fromUomId} to UOM {toUomId}.");
    }

    public async Task<decimal?> TryConvertAsync(decimal quantity, int fromUomId, int toUomId, int? partId, CancellationToken ct)
    {
        if (fromUomId == toUomId) return quantity;

        // Try part-specific conversion first
        if (partId.HasValue)
        {
            var partConversion = await db.Set<UomConversion>()
                .AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.PartId == partId &&
                    ((c.FromUomId == fromUomId && c.ToUomId == toUomId) ||
                     (c.IsReversible && c.FromUomId == toUomId && c.ToUomId == fromUomId)), ct);

            if (partConversion != null)
                return ApplyConversion(quantity, partConversion, fromUomId);
        }

        // Try global conversion
        var conversion = await db.Set<UomConversion>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.PartId == null &&
                ((c.FromUomId == fromUomId && c.ToUomId == toUomId) ||
                 (c.IsReversible && c.FromUomId == toUomId && c.ToUomId == fromUomId)), ct);

        if (conversion != null)
            return ApplyConversion(quantity, conversion, fromUomId);

        return null;
    }

    public async Task<IReadOnlyList<UomConversion>> GetConversionsAsync(int uomId, CancellationToken ct)
    {
        return await db.Set<UomConversion>()
            .AsNoTracking()
            .Include(c => c.FromUom)
            .Include(c => c.ToUom)
            .Where(c => c.FromUomId == uomId || (c.IsReversible && c.ToUomId == uomId))
            .OrderBy(c => c.FromUom.Code)
            .ThenBy(c => c.ToUom.Code)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<UnitOfMeasure>> GetByCategoryAsync(UomCategory category, CancellationToken ct)
    {
        return await db.Set<UnitOfMeasure>()
            .AsNoTracking()
            .Where(u => u.Category == category && u.IsActive)
            .OrderBy(u => u.SortOrder)
            .ToListAsync(ct);
    }

    private static decimal ApplyConversion(decimal quantity, UomConversion conversion, int fromUomId)
    {
        return conversion.FromUomId == fromUomId
            ? quantity * conversion.ConversionFactor
            : quantity / conversion.ConversionFactor;
    }
}
