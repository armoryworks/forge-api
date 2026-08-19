using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Sequences;

public record GetSequenceEventsQuery(int InstanceId) : IRequest<IReadOnlyList<SequenceEventResponseModel>>;

public class GetSequenceEventsHandler(AppDbContext db) : IRequestHandler<GetSequenceEventsQuery, IReadOnlyList<SequenceEventResponseModel>>
{
    public async Task<IReadOnlyList<SequenceEventResponseModel>> Handle(GetSequenceEventsQuery request, CancellationToken cancellationToken)
    {
        var events = await db.SequenceEvents.Where(e => e.InstanceId == request.InstanceId)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).ToListAsync(cancellationToken);
        return events.Select(SequenceMapping.ToModel).ToList();
    }
}
