using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Sequences;

public record GetSequenceDefinitionQuery(int Id) : IRequest<SequenceDefinitionResponseModel>;

public class GetSequenceDefinitionHandler(AppDbContext db) : IRequestHandler<GetSequenceDefinitionQuery, SequenceDefinitionResponseModel>
{
    public async Task<SequenceDefinitionResponseModel> Handle(GetSequenceDefinitionQuery request, CancellationToken cancellationToken)
    {
        var def = await db.SequenceDefinitions.WithGraph().FirstOrDefaultAsync(d => d.Id == request.Id && d.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException($"Sequence definition {request.Id} not found.");
        return SequenceMapping.ToModel(def);
    }
}
