using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Sequences;

/// <summary>Explicit re-evaluation (idempotent). Also the command every reaction dispatches: gate cleared, approval decided, clock fired.</summary>
public record ReevaluateSequenceCommand(int InstanceId, int? UserId) : IRequest<SequenceInstanceResponseModel>;

public class ReevaluateSequenceHandler(AppDbContext db, ISequenceEvaluationService evaluation) : IRequestHandler<ReevaluateSequenceCommand, SequenceInstanceResponseModel>
{
    public async Task<SequenceInstanceResponseModel> Handle(ReevaluateSequenceCommand request, CancellationToken cancellationToken)
    {
        await evaluation.EvaluateAsync(request.InstanceId, request.UserId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var i = await db.SequenceInstances.WithGraph().FirstAsync(x => x.Id == request.InstanceId, cancellationToken);
        return SequenceMapping.ToModel(i);
    }
}
