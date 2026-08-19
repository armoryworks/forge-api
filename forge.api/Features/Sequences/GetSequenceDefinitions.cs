using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Sequences;

public record GetSequenceDefinitionsQuery(string? Code = null, SequenceDefinitionStatus? Status = null) : IRequest<IReadOnlyList<SequenceDefinitionResponseModel>>;

public class GetSequenceDefinitionsHandler(AppDbContext db) : IRequestHandler<GetSequenceDefinitionsQuery, IReadOnlyList<SequenceDefinitionResponseModel>>
{
    public async Task<IReadOnlyList<SequenceDefinitionResponseModel>> Handle(GetSequenceDefinitionsQuery request, CancellationToken cancellationToken)
    {
        var q = db.SequenceDefinitions.WithGraph().Where(d => d.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(request.Code)) q = q.Where(d => d.Code == request.Code);
        if (request.Status.HasValue) q = q.Where(d => d.Status == request.Status);
        var list = await q.OrderBy(d => d.Code).ThenByDescending(d => d.Version).ToListAsync(cancellationToken);
        return list.Select(SequenceMapping.ToModel).ToList();
    }
}
