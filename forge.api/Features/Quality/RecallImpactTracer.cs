using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Lots;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

/// <summary>
/// Computes the blast radius of a recall on a lot without changing anything: the lot and every downstream
/// produced lot (forward over lot_consumptions), the Stored on-hand of those lots that would go on QC hold,
/// and the shipments and customers that received them (lot-stamped Ship movements, unioned with the
/// approximate job → sales-order-line chain). Initiating a recall freezes exactly this result, so the
/// preview and the recorded snapshot cannot disagree.
/// </summary>
internal static class RecallImpactTracer
{
    public static async Task<RecallImpact> TraceAsync(AppDbContext db, int recalledLotId, CancellationToken ct)
    {
        var rootLot = await db.LotRecords
            .IgnoreQueryFilters()
            .Include(l => l.Part)
            .FirstOrDefaultAsync(l => l.Id == recalledLotId, ct)
            ?? throw new KeyNotFoundException($"Lot {recalledLotId} not found.");

        var affectedLotIds = await ForwardReachableAsync(db, rootLot.Id, ct);
        affectedLotIds.Add(rootLot.Id);

        var affectedLots = await db.LotRecords
            .IgnoreQueryFilters()
            .Include(l => l.Part)
            .Where(l => affectedLotIds.Contains(l.Id))
            .ToListAsync(ct);

        var consumedByLot = await db.LotConsumptions
            .Where(c => affectedLotIds.Contains(c.ProducedLotId))
            .GroupBy(c => c.ProducedLotId)
            .Select(g => new { ProducedLotId = g.Key, Qty = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.ProducedLotId, x => x.Qty, ct);

        var affectedLotNumbers = affectedLots.Select(l => l.LotNumber).ToList();
        var affectedPartIds = affectedLots.Select(l => l.PartId).Distinct().ToList();
        var affectedPartLots = affectedLots.Select(l => (l.PartId, l.LotNumber)).ToHashSet();

        var binContents = (await db.BinContents
            .Where(bc => bc.EntityType == "part"
                      && affectedPartIds.Contains(bc.EntityId)
                      && bc.LotNumber != null && affectedLotNumbers.Contains(bc.LotNumber)
                      && bc.RemovedAt == null
                      && bc.Status == BinContentStatus.Stored)
            .ToListAsync(ct))
            .Where(bc => affectedPartLots.Contains((bc.EntityId, bc.LotNumber!)))
            .ToList();

        var exactShipments = await LotShipmentResolver.ForLotsAsync(db, affectedLotNumbers, ct);
        var issuedJobIds = await LotMaterialIssues.WithParentJobsAsync(
            db, await LotMaterialIssues.JobIdsAsync(db, affectedPartLots, ct), ct);
        var soLineIds = await AffectedSalesOrderLineIdsAsync(
            db, affectedLots.Select(l => l.JobId).Concat(issuedJobIds.Select(id => (int?)id)), ct);
        var approximateShipments = await LotShipmentResolver.ForSalesOrderLinesAsync(
            db, soLineIds, affectedLotNumbers, ct);

        var shipments = exactShipments.Concat(approximateShipments)
            .GroupBy(r => new { r.ShipmentId, r.CustomerId })
            .Select(g => new RecallImpactShipment(
                g.Key.ShipmentId,
                g.Select(x => x.ShipmentNumber).First(),
                g.Key.CustomerId,
                g.Select(x => x.CustomerName).First(),
                g.Sum(x => x.Quantity),
                g.Max(x => x.ShippedDate),
                g.Select(x => x.TrackingNumber).FirstOrDefault(),
                g.Any(x => x.IsApproximate)))
            .ToList();

        return new RecallImpact(rootLot, affectedLots, consumedByLot, binContents, shipments);
    }

    private static async Task<List<int>> AffectedSalesOrderLineIdsAsync(
        AppDbContext db, IEnumerable<int?> jobIds, CancellationToken ct)
    {
        var ids = jobIds.Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0)
            return [];

        return await db.Jobs
            .IgnoreQueryFilters()
            .Where(j => ids.Contains(j.Id) && j.SalesOrderLineId != null)
            .Select(j => j.SalesOrderLineId!.Value)
            .Distinct()
            .ToListAsync(ct);
    }

    private static async Task<HashSet<int>> ForwardReachableAsync(AppDbContext db, int start, CancellationToken ct)
    {
        var reachable = new HashSet<int>();
        var frontier = new Queue<int>();
        frontier.Enqueue(start);
        while (frontier.Count > 0)
        {
            var node = frontier.Dequeue();
            var next = await db.LotConsumptions
                .Where(c => c.ConsumedLotId == node)
                .Select(c => c.ProducedLotId)
                .ToListAsync(ct);
            foreach (var n in next)
                if (reachable.Add(n))
                    frontier.Enqueue(n);
        }
        return reachable;
    }
}

/// <summary>The traced impact of a recall on a lot; the bin contents are tracked, so the initiate handler can put them on hold.</summary>
internal sealed record RecallImpact(
    LotRecord RootLot,
    List<LotRecord> AffectedLots,
    Dictionary<int, decimal> ConsumedByLot,
    List<BinContent> QuarantineBins,
    List<RecallImpactShipment> Shipments)
{
    public Dictionary<string, decimal> OnHandByLot { get; } = QuarantineBins
        .GroupBy(bc => bc.LotNumber!)
        .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

    public decimal OnHandFor(LotRecord lot) => OnHandByLot.TryGetValue(lot.LotNumber, out var qty) ? qty : 0m;

    public decimal ConsumedFor(LotRecord lot) => ConsumedByLot.TryGetValue(lot.Id, out var qty) ? qty : 0m;
}

/// <summary>A shipment (and its customer) that received an affected lot.</summary>
internal sealed record RecallImpactShipment(
    int ShipmentId,
    string ShipmentNumber,
    int CustomerId,
    string CustomerName,
    decimal AffectedQuantity,
    DateTimeOffset? ShippedDate,
    string? TrackingNumber,
    bool IsApproximate);
