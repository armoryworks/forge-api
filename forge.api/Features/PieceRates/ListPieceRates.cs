using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Models.PieceRates;
using Forge.Data.Context;

namespace Forge.Api.Features.PieceRates;

/// <summary>Every piece-rate scope (part / part+operation) with its current rate and full timeline.</summary>
[RequiresCapability("CAP-HR-PIECE-RATES")]
public record ListPieceRatesQuery : IRequest<IReadOnlyList<PieceRateTimelineModel>>;

public class ListPieceRatesHandler(AppDbContext db)
    : IRequestHandler<ListPieceRatesQuery, IReadOnlyList<PieceRateTimelineModel>>
{
    public async Task<IReadOnlyList<PieceRateTimelineModel>> Handle(ListPieceRatesQuery request, CancellationToken ct)
    {
        var rows = await db.PieceRates.AsNoTracking()
            .Include(r => r.Part)
            .OrderBy(r => r.Part!.PartNumber).ThenBy(r => r.OperationId).ThenByDescending(r => r.EffectiveFrom)
            .ToListAsync(ct);

        return rows
            .GroupBy(r => (r.PartId, r.OperationId))
            .Select(g =>
            {
                var models = g.Select(r => new PieceRateModel(
                    r.Id, r.PartId, r.Part!.PartNumber, r.Part.Description, r.OperationId,
                    r.RatePerPiece, r.EffectiveFrom, r.EffectiveTo, r.Notes)).ToList();
                var first = g.First();
                return new PieceRateTimelineModel(
                    first.PartId, first.Part!.PartNumber, first.Part.Description, first.OperationId,
                    models.FirstOrDefault(x => x.EffectiveTo is null),
                    models);
            })
            .OrderBy(t => t.PartNumber).ThenBy(t => t.OperationId)
            .ToList();
    }
}
