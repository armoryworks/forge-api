using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.DomainEvents;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

/// <summary>
/// Daily Hangfire job — finds parts where available stock is at or below
/// the reorder point and publishes InventoryBelowReorderEvent for each.
/// </summary>
public class CheckInventoryLevelsJob(
    AppDbContext db,
    IPublisher publisher,
    ILogger<CheckInventoryLevelsJob> logger)
{
    private const int ChunkSize = 500;

    public async Task Execute(CancellationToken ct)
    {
        // Get parts that have a reorder point defined
        var partsWithReorder = await db.Parts
            .Where(p => p.ReorderPoint.HasValue && p.ReorderPoint > 0 && p.DeletedAt == null)
            .Select(p => new { p.Id, ReorderPoint = p.ReorderPoint!.Value })
            .AsNoTracking()
            .ToListAsync(ct);

        if (partsWithReorder.Count == 0)
        {
            logger.LogInformation("[CheckInventoryLevels] No parts with reorder points — skipping");
            return;
        }

        var eventCount = 0;

        // Process in chunks to avoid loading too much data
        for (var offset = 0; offset < partsWithReorder.Count; offset += ChunkSize)
        {
            ct.ThrowIfCancellationRequested();

            var chunk = partsWithReorder.Skip(offset).Take(ChunkSize).ToList();
            var partIds = chunk.Select(p => p.Id).ToList();

            var stockByPart = await db.BinContents
                .Where(bc => bc.EntityType == "part"
                    && partIds.Contains(bc.EntityId)
                    && bc.RemovedAt == null)
                .GroupBy(bc => bc.EntityId)
                .Select(g => new
                {
                    PartId = g.Key,
                    Available = g.Sum(bc => bc.Quantity) - g.Sum(bc => bc.ReservedQuantity),
                })
                .AsNoTracking()
                .ToListAsync(ct);

            var stockMap = stockByPart.ToDictionary(s => s.PartId);

            foreach (var part in chunk)
            {
                var available = stockMap.TryGetValue(part.Id, out var s)
                    ? (int)s.Available
                    : 0;

                if (available <= (int)part.ReorderPoint)
                {
                    await publisher.Publish(
                        new InventoryBelowReorderEvent(part.Id, available, (int)part.ReorderPoint), ct);
                    eventCount++;
                }
            }
        }

        logger.LogInformation("[CheckInventoryLevels] Published {Count} below-reorder events", eventCount);
    }
}
