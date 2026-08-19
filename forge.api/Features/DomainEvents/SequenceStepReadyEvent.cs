using MediatR;

namespace Forge.Api.Features.DomainEvents;

/// <summary>Gated Sequence Engine — a step became Ready (all predecessors done, every gate Go). Consumers: notifications, andon, routing UIs.</summary>
public record SequenceStepReadyEvent(int InstanceId, string StepKey, string? SubjectEntityType, int? SubjectEntityId) : INotification;
