using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Sequences;

public record GetSequenceInstanceQuery(int Id) : IRequest<SequenceInstanceResponseModel>;

public class GetSequenceInstanceHandler(AppDbContext db) : IRequestHandler<GetSequenceInstanceQuery, SequenceInstanceResponseModel>
{
    public async Task<SequenceInstanceResponseModel> Handle(GetSequenceInstanceQuery request, CancellationToken cancellationToken)
    {
        var i = await db.SequenceInstances.WithGraph().FirstOrDefaultAsync(x => x.Id == request.Id && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException($"Sequence instance {request.Id} not found.");
        return SequenceMapping.ToModel(i);
    }
}
