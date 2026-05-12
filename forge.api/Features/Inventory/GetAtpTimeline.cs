using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record GetAtpTimelineQuery(int PartId, DateOnly? From, DateOnly? To) : IRequest<List<AtpBucket>>;

public class GetAtpTimelineHandler(IAtpService atpService, IClock clock) : IRequestHandler<GetAtpTimelineQuery, List<AtpBucket>>
{
    public async Task<List<AtpBucket>> Handle(GetAtpTimelineQuery request, CancellationToken cancellationToken)
    {
        var from = request.From ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var to = request.To ?? from.AddDays(90);

        return await atpService.GetAtpTimelineAsync(request.PartId, from, to, cancellationToken);
    }
}
