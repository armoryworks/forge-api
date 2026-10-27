using FluentValidation;
using MediatR;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

/// <summary>
/// CAP-QC-RECALL — what initiating a recall on the lot would record (affected lots, downstream lots,
/// shipments and customers, on-hand to quarantine), computed by the same trace as
/// <see cref="InitiateRecallCommand"/> but without saving the recall or putting stock on hold.
/// </summary>
public record PreviewRecallQuery(PreviewRecallRequestModel Data) : IRequest<RecallImpactPreviewResponseModel>;

public class PreviewRecallQueryValidator : AbstractValidator<PreviewRecallQuery>
{
    public PreviewRecallQueryValidator()
    {
        RuleFor(x => x.Data.RecalledLotId).GreaterThan(0);
    }
}

public class PreviewRecallHandler(AppDbContext db)
    : IRequestHandler<PreviewRecallQuery, RecallImpactPreviewResponseModel>
{
    public async Task<RecallImpactPreviewResponseModel> Handle(
        PreviewRecallQuery request, CancellationToken cancellationToken)
    {
        var impact = await RecallImpactTracer.TraceAsync(db, request.Data.RecalledLotId, cancellationToken);
        db.ChangeTracker.Clear();

        var lots = impact.AffectedLots
            .Select(l => new RecallAffectedLotModel(
                l.Id, l.LotNumber, l.Part.PartNumber, impact.ConsumedFor(l), l.JobId,
                impact.OnHandFor(l), impact.OnHandFor(l)))
            .ToList();
        var shipments = impact.Shipments
            .Select(s => new RecallAffectedShipmentModel(
                s.ShipmentId, s.ShipmentNumber, s.CustomerId, s.CustomerName,
                s.AffectedQuantity, s.ShippedDate, s.TrackingNumber, s.IsApproximate))
            .ToList();

        return new RecallImpactPreviewResponseModel(
            impact.RootLot.Id,
            impact.RootLot.LotNumber,
            lots.Count,
            shipments.Count,
            shipments.Select(s => s.CustomerId).Distinct().Count(),
            impact.QuarantineBins.Sum(bc => bc.Quantity),
            lots,
            shipments);
    }
}
