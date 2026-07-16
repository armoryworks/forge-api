using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Lots;

/// <summary>
/// regulated-parts-safety C-2: records component-genealogy edges — the input lots
/// consumed to produce an output lot. Idempotent per (consumed, produced) pair, and
/// guarded against directed cycles so the genealogy stays a DAG. This is the write
/// path that was previously missing (the table/entity existed but nothing populated
/// them); it unblocks CAP-QC-RECALL forward/backward tracing.
/// </summary>
public record RecordLotConsumptionCommand(int ProducedLotId, RecordLotConsumptionRequestModel Data)
    : IRequest<List<LotConsumptionEdgeModel>>;

public class RecordLotConsumptionCommandValidator : AbstractValidator<RecordLotConsumptionCommand>
{
    public RecordLotConsumptionCommandValidator()
    {
        RuleFor(x => x.ProducedLotId).GreaterThan(0);
        RuleFor(x => x.Data.Consumptions).NotEmpty();
        RuleForEach(x => x.Data.Consumptions).ChildRules(c =>
        {
            c.RuleFor(i => i.ConsumedLotId).GreaterThan(0);
            c.RuleFor(i => i.Quantity).GreaterThan(0m);
        });
    }
}

public class RecordLotConsumptionHandler(AppDbContext db)
    : IRequestHandler<RecordLotConsumptionCommand, List<LotConsumptionEdgeModel>>
{
    public async Task<List<LotConsumptionEdgeModel>> Handle(
        RecordLotConsumptionCommand request, CancellationToken cancellationToken)
    {
        var producedLotId = request.ProducedLotId;
        var producedLot = await db.LotRecords
            .FirstOrDefaultAsync(l => l.Id == producedLotId, cancellationToken)
            ?? throw new KeyNotFoundException($"Produced lot {producedLotId} not found.");

        // Collapse duplicates and drop any self-reference before validating.
        var requested = request.Data.Consumptions
            .Where(c => c.ConsumedLotId != producedLotId)
            .GroupBy(c => c.ConsumedLotId)
            .Select(g => new { ConsumedLotId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();
        if (requested.Count == 0)
            throw new InvalidOperationException("A lot cannot consume itself; no valid consumed lots supplied.");

        var consumedIds = requested.Select(r => r.ConsumedLotId).ToList();
        var foundIds = await db.LotRecords
            .Where(l => consumedIds.Contains(l.Id))
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);
        var missing = consumedIds.Except(foundIds).ToList();
        if (missing.Count > 0)
            throw new KeyNotFoundException($"Consumed lot(s) not found: {string.Join(", ", missing)}.");

        // Cycle guard: an edge (consumed → produced) closes a directed cycle iff the
        // produced lot can already reach that consumed lot by following forward edges.
        var reachableFromProduced = await ForwardReachableAsync(producedLotId, cancellationToken);
        var cyclic = requested.Where(r => reachableFromProduced.Contains(r.ConsumedLotId))
            .Select(r => r.ConsumedLotId).ToList();
        if (cyclic.Count > 0)
            throw new InvalidOperationException(
                $"Recording these consumptions would create a genealogy cycle: {string.Join(", ", cyclic)}.");

        // Idempotent: skip (consumed, produced) pairs that already exist.
        var existingPairs = await db.LotConsumptions
            .Where(c => c.ProducedLotId == producedLotId && consumedIds.Contains(c.ConsumedLotId))
            .Select(c => c.ConsumedLotId)
            .ToListAsync(cancellationToken);

        foreach (var r in requested.Where(r => !existingPairs.Contains(r.ConsumedLotId)))
        {
            db.LotConsumptions.Add(new LotConsumption
            {
                ConsumedLotId = r.ConsumedLotId,
                ProducedLotId = producedLotId,
                Quantity = r.Quantity,
                JobId = request.Data.JobId ?? producedLot.JobId,
                ProductionRunId = request.Data.ProductionRunId ?? producedLot.ProductionRunId,
            });
        }
        await db.SaveChangesAsync(cancellationToken);

        return await db.LotConsumptions
            .AsNoTracking()
            .Where(c => c.ProducedLotId == producedLotId)
            .Include(c => c.ConsumedLot).ThenInclude(l => l.Part)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new LotConsumptionEdgeModel(
                c.Id,
                c.ConsumedLotId,
                c.ConsumedLot.LotNumber,
                c.ConsumedLot.PartId,
                c.ConsumedLot.Part.PartNumber,
                c.Quantity,
                c.JobId,
                c.ProductionRunId,
                c.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// BFS over forward edges (rows where <c>ConsumedLotId == node</c> point to their
    /// <c>ProducedLotId</c>) to collect every lot reachable downstream of <paramref name="start"/>.
    /// The visited set makes it terminate even on already-cyclic legacy data.
    /// </summary>
    private async Task<HashSet<int>> ForwardReachableAsync(int start, CancellationToken ct)
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
