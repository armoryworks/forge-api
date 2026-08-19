using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Sequences;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Sequences;

/// <summary>Terminal cancel; reason required.</summary>
public record CancelSequenceInstanceCommand(int InstanceId, string Reason, int UserId) : IRequest<SequenceInstanceResponseModel>;

public class CancelSequenceInstanceHandler(AppDbContext db, IClock clock) : IRequestHandler<CancelSequenceInstanceCommand, SequenceInstanceResponseModel>
{
    public async Task<SequenceInstanceResponseModel> Handle(CancelSequenceInstanceCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("A cancel reason is required.");
        var i = await db.SequenceInstances.WithGraph().FirstOrDefaultAsync(x => x.Id == request.InstanceId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException($"Sequence instance {request.InstanceId} not found.");
        if (i.Status != SequenceInstanceStatus.Running) throw new InvalidOperationException($"Instance is already {i.Status}.");

        var now = clock.UtcNow;
        i.Status = SequenceInstanceStatus.Cancelled;
        i.CancelledAt = now;
        i.CancelledByUserId = request.UserId;
        i.CancelReason = request.Reason.Trim();
        db.SequenceEvents.Add(SequenceEvaluator.Event(i, SequenceEventType.InstanceCancelled, now, request.UserId,
            payloadJson: $"{{\"reason\":\"{i.CancelReason.Replace("\"", "\\\"")}\"}}"));
        db.LogActivityAt("sequence-cancelled", $"Sequence {i.Definition!.Code} cancelled: {i.CancelReason}", SequenceQueries.IndexingPoints(i));
        await db.SaveChangesAsync(cancellationToken);
        return SequenceMapping.ToModel(i);
    }
}
