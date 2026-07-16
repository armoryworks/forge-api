using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

/// <summary>
/// CAP-QC-RECALL — initiates a lot-based recall. Walks the lot_consumptions genealogy FORWARD
/// from the recalled lot to every downstream produced lot, quarantines matching on-hand bin
/// contents (Stored → QcHold), resolves the shipments/customers that received affected lots
/// (via job → sales-order-line → shipment-line, i.e. SO-line granularity), and freezes it all
/// as an immutable Recall snapshot.
/// </summary>
public record InitiateRecallCommand(InitiateRecallRequestModel Data) : IRequest<RecallDetailResponseModel>;

public class InitiateRecallCommandValidator : AbstractValidator<InitiateRecallCommand>
{
    public InitiateRecallCommandValidator()
    {
        RuleFor(x => x.Data.RecalledLotId).GreaterThan(0);
        RuleFor(x => x.Data.Reason).NotEmpty().MaximumLength(2000);
    }
}

public class InitiateRecallHandler(AppDbContext db, IHttpContextAccessor httpContext)
    : IRequestHandler<InitiateRecallCommand, RecallDetailResponseModel>
{
    public async Task<RecallDetailResponseModel> Handle(
        InitiateRecallCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var rootLot = await db.LotRecords
            .FirstOrDefaultAsync(l => l.Id == data.RecalledLotId, cancellationToken)
            ?? throw new KeyNotFoundException($"Lot {data.RecalledLotId} not found.");

        // 1) Forward-trace the genealogy: the recalled lot + every produced lot downstream of it.
        var affectedLotIds = await ForwardReachableAsync(rootLot.Id, cancellationToken);
        affectedLotIds.Add(rootLot.Id);

        var affectedLots = await db.LotRecords
            .Where(l => affectedLotIds.Contains(l.Id))
            .ToListAsync(cancellationToken);

        var consumedByLot = await db.LotConsumptions
            .Where(c => affectedLotIds.Contains(c.ProducedLotId))
            .GroupBy(c => c.ProducedLotId)
            .Select(g => new { ProducedLotId = g.Key, Qty = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.ProducedLotId, x => x.Qty, cancellationToken);

        var affectedLotNumbers = affectedLots.Select(l => l.LotNumber).ToList();

        // 2) Quarantine on-hand: matching part-stock bin contents currently Stored → QcHold.
        var binContents = await db.BinContents
            .Where(bc => bc.LotNumber != null && affectedLotNumbers.Contains(bc.LotNumber)
                      && bc.Status == BinContentStatus.Stored)
            .ToListAsync(cancellationToken);
        foreach (var bc in binContents)
            bc.Status = BinContentStatus.QcHold;

        var onHandByLot = binContents
            .GroupBy(bc => bc.LotNumber!)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        // 3) Resolve affected shipments/customers: lot → job → sales-order-line → shipment-line.
        var affectedJobIds = affectedLots
            .Where(l => l.JobId != null).Select(l => l.JobId!.Value).Distinct().ToList();
        var soLineIds = await db.Jobs
            .Where(j => affectedJobIds.Contains(j.Id) && j.SalesOrderLineId != null)
            .Select(j => j.SalesOrderLineId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var shipmentRows = await db.ShipmentLines
            .Where(sl => sl.SalesOrderLineId != null && soLineIds.Contains(sl.SalesOrderLineId.Value))
            .Select(sl => new
            {
                sl.ShipmentId,
                sl.Shipment.ShipmentNumber,
                sl.Shipment.ShippedDate,
                sl.Shipment.TrackingNumber,
                sl.Shipment.SalesOrder.CustomerId,
                sl.Quantity,
            })
            .ToListAsync(cancellationToken);

        var shipmentGroups = shipmentRows
            .GroupBy(r => new { r.ShipmentId, r.CustomerId })
            .Select(g => new
            {
                g.Key.ShipmentId,
                g.Key.CustomerId,
                ShippedDate = g.Max(x => x.ShippedDate),
                TrackingNumber = g.Select(x => x.TrackingNumber).FirstOrDefault(),
                AffectedQty = g.Sum(x => x.Quantity),
            })
            .ToList();

        // 4) Freeze the immutable snapshot.
        var recall = new Recall
        {
            InitiatedByUserId = userId,
            InitiatedLotId = rootLot.Id,
            Reason = data.Reason.Trim(),
            RecallDate = data.RecallDate,
            Status = RecallStatus.Active,
            AffectedLotsCount = affectedLots.Count,
            AffectedShipmentsCount = shipmentGroups.Count,
            TotalQuarantinedQuantity = binContents.Sum(bc => bc.Quantity),
        };

        foreach (var lot in affectedLots)
        {
            var onHand = onHandByLot.TryGetValue(lot.LotNumber, out var oh) ? oh : 0m;
            recall.AffectedLots.Add(new RecallAffectedLot
            {
                LotId = lot.Id,
                ConsumedQuantity = consumedByLot.TryGetValue(lot.Id, out var cq) ? cq : 0m,
                JobId = lot.JobId,
                ProductionRunId = lot.ProductionRunId,
                OnHandQuantity = onHand,
                QuarantinedQuantity = onHand, // all matching on-hand was moved to QcHold above
            });
        }

        foreach (var s in shipmentGroups)
        {
            recall.AffectedShipments.Add(new RecallAffectedShipment
            {
                ShipmentId = s.ShipmentId,
                CustomerId = s.CustomerId,
                AffectedQuantity = s.AffectedQty,
                ShippedDate = s.ShippedDate,
                TrackingNumber = s.TrackingNumber,
            });
        }

        db.Recalls.Add(recall);
        await db.SaveChangesAsync(cancellationToken);

        return await RecallMapping.LoadDetailAsync(db, recall.Id, cancellationToken);
    }

    /// <summary>BFS over forward edges (ConsumedLotId == node → ProducedLotId) to collect the blast radius.</summary>
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
