using MediatR;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipping;

public record GetMyPickLinesQuery(int UserId) : IRequest<List<PickLineResponseModel>>;

public class GetMyPickLinesHandler(AppDbContext db) : IRequestHandler<GetMyPickLinesQuery, List<PickLineResponseModel>>
{
    public Task<List<PickLineResponseModel>> Handle(GetMyPickLinesQuery request, CancellationToken cancellationToken) =>
        PickLineQueries.ListAsync(db, db.PickLines.Where(l =>
            l.Status == PickLineStatus.Pending
            && (l.Wave.Status == PickWaveStatus.Released || l.Wave.Status == PickWaveStatus.InProgress)
            && (l.Wave.AssignedToId == null || l.Wave.AssignedToId == request.UserId)), cancellationToken);
}
