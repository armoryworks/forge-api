using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Sequences;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

/// <summary>
/// Gated Sequence Engine reaction: when an approval reaches a terminal decision, re-evaluate every running instance
/// that has an Approval gate pointed at that entity — explicitly via config or via the instance's subject.
/// </summary>
public class OnApprovalCompleted_ReevaluateSequences(AppDbContext db, IMediator mediator) : INotificationHandler<ApprovalCompletedEvent>
{
    public async Task Handle(ApprovalCompletedEvent notification, CancellationToken cancellationToken)
    {
        var candidates = await db.SequenceInstances
            .Where(i => i.Status == SequenceInstanceStatus.Running && i.DeletedAt == null)
            .Where(i => i.Definition!.Gates.Any(g => g.SourceType == SequenceGateSourceType.Approval))
            .Select(i => new { i.Id, i.SubjectEntityType, i.SubjectEntityId,
                Configs = i.Definition!.Gates.Where(g => g.SourceType == SequenceGateSourceType.Approval).Select(g => g.ConfigJson).ToList() })
            .ToListAsync(cancellationToken);

        foreach (var c in candidates)
        {
            var hit = c.Configs.Any(json =>
            {
                var cfg = Forge.Api.Features.Sequences.GateSources.SequenceGateConfig.Parse(json);
                var type = cfg.GetBool("fromSubject") ? c.SubjectEntityType : cfg.GetString("entityType");
                var id = cfg.GetBool("fromSubject") ? c.SubjectEntityId : cfg.GetInt("entityId");
                return type == notification.EntityType && id == notification.EntityId;
            });
            if (hit) await mediator.Send(new ReevaluateSequenceCommand(c.Id, notification.DecidedById), cancellationToken);
        }
    }
}
