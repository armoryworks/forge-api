using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

/// <summary>Shared loader that maps a persisted recall snapshot to its detail response.</summary>
internal static class RecallMapping
{
    public static async Task<RecallDetailResponseModel> LoadDetailAsync(
        AppDbContext db, int recallId, CancellationToken ct)
    {
        var recall = await db.Recalls
            .AsNoTracking()
            .Include(r => r.InitiatedLot)
            .Include(r => r.AffectedLots).ThenInclude(al => al.Lot).ThenInclude(l => l.Part)
            .Include(r => r.AffectedShipments).ThenInclude(s => s.Shipment)
            .Include(r => r.AffectedShipments).ThenInclude(s => s.Customer)
            .FirstOrDefaultAsync(r => r.Id == recallId, ct)
            ?? throw new KeyNotFoundException($"Recall {recallId} not found.");

        return new RecallDetailResponseModel(
            recall.Id,
            recall.InitiatedLotId,
            recall.InitiatedLot.LotNumber,
            recall.Reason,
            recall.RecallDate,
            recall.Status,
            recall.AffectedLotsCount,
            recall.AffectedShipmentsCount,
            recall.TotalQuarantinedQuantity,
            recall.ResolvedAt,
            recall.ResolutionNotes,
            recall.AffectedLots
                .Select(al => new RecallAffectedLotModel(
                    al.LotId, al.Lot.LotNumber, al.Lot.Part.PartNumber,
                    al.ConsumedQuantity, al.JobId, al.OnHandQuantity, al.QuarantinedQuantity))
                .ToList(),
            recall.AffectedShipments
                .Select(s => new RecallAffectedShipmentModel(
                    s.ShipmentId, s.Shipment.ShipmentNumber, s.CustomerId, s.Customer.Name,
                    s.AffectedQuantity, s.ShippedDate, s.TrackingNumber))
                .ToList(),
            recall.CreatedAt);
    }
}
