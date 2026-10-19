using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public static class JobSalesPrice
{
    public static decimal Resolve(decimal? orderLineUnitPrice, decimal quantity, decimal quotedPrice) =>
        orderLineUnitPrice is decimal unitPrice ? unitPrice * quantity : quotedPrice;

    public static async Task<Dictionary<int, decimal>> ResolveAsync(
        AppDbContext db, IReadOnlyCollection<int> jobIds, CancellationToken ct)
    {
        var rows = await db.Jobs.AsNoTracking()
            .Where(j => jobIds.Contains(j.Id))
            .Select(j => new
            {
                j.Id,
                j.QuotedPrice,
                UnitPrice = j.SalesOrderLine != null ? (decimal?)j.SalesOrderLine.UnitPrice : null,
                LineQuantity = j.SalesOrderLine != null ? (decimal?)j.SalesOrderLine.Quantity : null,
                BuildQuantity = j.JobParts
                    .Where(jp => jp.PartId == j.PartId)
                    .Select(jp => (decimal?)jp.Quantity)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return rows.ToDictionary(
            r => r.Id,
            r => Resolve(r.UnitPrice, r.BuildQuantity ?? r.LineQuantity ?? 1m, r.QuotedPrice));
    }
}
