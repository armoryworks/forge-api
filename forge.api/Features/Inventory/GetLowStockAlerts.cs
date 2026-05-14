using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record GetLowStockAlertsQuery : IRequest<List<LowStockAlertModel>>;

public class GetLowStockAlertsHandler(AppDbContext db)
    : IRequestHandler<GetLowStockAlertsQuery, List<LowStockAlertModel>>
{
    public async Task<List<LowStockAlertModel>> Handle(GetLowStockAlertsQuery request, CancellationToken ct)
    {
        var partsWithThreshold = await db.Parts
            .Where(p => p.MinStockThreshold.HasValue && p.Status == PartStatus.Active)
            // Only alert for parts that have been received at least once —
            // parts with thresholds but no bin history are a setup/purchasing concern, not a low-stock alert
            .Where(p => db.BinContents.Any(bc => bc.EntityType == "part" && bc.EntityId == p.Id))
            .Select(p => new
            {
                p.Id,
                p.PartNumber,
                p.Name,
                p.Description,
                p.MinStockThreshold,
                p.ReorderPoint,
                CurrentStock = db.BinContents
                    .Where(bc => bc.EntityType == "part" && bc.EntityId == p.Id && bc.Status == BinContentStatus.Stored)
                    .Sum(bc => (decimal?)bc.Quantity) ?? 0m,
            })
            .ToListAsync(ct);

        return partsWithThreshold
            .Where(p => p.CurrentStock < p.MinStockThreshold!.Value)
            .Select(p => new LowStockAlertModel(
                p.Id,
                p.PartNumber,
                p.Description ?? p.Name,
                p.CurrentStock,
                p.MinStockThreshold!.Value,
                p.ReorderPoint))
            .OrderBy(p => p.CurrentStock)
            .ToList();
    }
}
