using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

/// <summary>
/// CAP-QC-RECALL — initiates a lot-based recall. Walks the lot_consumptions genealogy FORWARD
/// from the recalled lot to every downstream produced lot, quarantines matching on-hand bin
/// contents of the affected parts and lots (Stored → QcHold), resolves the shipments/customers that received
/// affected lots (from the lot-stamped Ship bin movements, unioned with the job → sales-order-line chain of the
/// jobs that produced an affected lot or were issued one (and their parent jobs), flagged approximate, for every shipped line of that
/// order line whose Ship movements carry no affected lot: unlotted, stamped with another lot by FIFO relief, or
/// never relieved), and freezes it all as an immutable Recall snapshot.
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

        var impact = await RecallImpactTracer.TraceAsync(db, data.RecalledLotId, cancellationToken);
        foreach (var bc in impact.QuarantineBins)
            bc.Status = BinContentStatus.QcHold;

        var recall = new Recall
        {
            InitiatedByUserId = userId,
            InitiatedLotId = impact.RootLot.Id,
            Reason = data.Reason.Trim(),
            RecallDate = data.RecallDate,
            Status = RecallStatus.Active,
            AffectedLotsCount = impact.AffectedLots.Count,
            AffectedShipmentsCount = impact.Shipments.Count,
            TotalQuarantinedQuantity = impact.QuarantineBins.Sum(bc => bc.Quantity),
        };

        foreach (var lot in impact.AffectedLots)
        {
            var onHand = impact.OnHandFor(lot);
            recall.AffectedLots.Add(new RecallAffectedLot
            {
                LotId = lot.Id,
                ConsumedQuantity = impact.ConsumedFor(lot),
                JobId = lot.JobId,
                ProductionRunId = lot.ProductionRunId,
                OnHandQuantity = onHand,
                QuarantinedQuantity = onHand,
            });
        }

        foreach (var s in impact.Shipments)
        {
            recall.AffectedShipments.Add(new RecallAffectedShipment
            {
                ShipmentId = s.ShipmentId,
                CustomerId = s.CustomerId,
                AffectedQuantity = s.AffectedQuantity,
                ShippedDate = s.ShippedDate,
                TrackingNumber = s.TrackingNumber,
                IsApproximate = s.IsApproximate,
            });
        }

        db.Recalls.Add(recall);
        await db.SaveChangesAsync(cancellationToken);

        return await RecallMapping.LoadDetailAsync(db, recall.Id, cancellationToken);
    }
}
