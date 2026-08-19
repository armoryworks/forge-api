using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Sequences;

public record GetSequenceInstancesQuery(string? SubjectEntityType, int? SubjectEntityId, SequenceInstanceStatus? Status, int? DefinitionId)
    : IRequest<IReadOnlyList<SequenceInstanceResponseModel>>;

public class GetSequenceInstancesHandler(AppDbContext db) : IRequestHandler<GetSequenceInstancesQuery, IReadOnlyList<SequenceInstanceResponseModel>>
{
    public async Task<IReadOnlyList<SequenceInstanceResponseModel>> Handle(GetSequenceInstancesQuery request, CancellationToken cancellationToken)
    {
        var q = db.SequenceInstances.WithGraph().Where(i => i.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(request.SubjectEntityType)) q = q.Where(i => i.SubjectEntityType == request.SubjectEntityType);
        if (request.SubjectEntityId.HasValue) q = q.Where(i => i.SubjectEntityId == request.SubjectEntityId);
        if (request.Status.HasValue) q = q.Where(i => i.Status == request.Status);
        if (request.DefinitionId.HasValue) q = q.Where(i => i.DefinitionId == request.DefinitionId);
        var list = await q.OrderByDescending(i => i.StartedAt).Take(500).ToListAsync(cancellationToken);
        return list.Select(SequenceMapping.ToModel).ToList();
    }
}
