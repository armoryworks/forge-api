using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetRecallsQuery(RecallStatus? Status) : IRequest<List<RecallResponseModel>>;

public class GetRecallsHandler(AppDbContext db) : IRequestHandler<GetRecallsQuery, List<RecallResponseModel>>
{
    public async Task<List<RecallResponseModel>> Handle(GetRecallsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Recalls.AsNoTracking().Include(r => r.InitiatedLot).AsQueryable();
        if (request.Status.HasValue)
            query = query.Where(r => r.Status == request.Status.Value);

        return await query
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new RecallResponseModel(
                r.Id,
                r.InitiatedLotId,
                r.InitiatedLot.LotNumber,
                r.Reason,
                r.RecallDate,
                r.Status,
                r.AffectedLotsCount,
                r.AffectedShipmentsCount,
                r.TotalQuarantinedQuantity,
                r.ResolvedAt,
                r.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
