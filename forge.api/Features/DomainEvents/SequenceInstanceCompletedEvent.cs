using MediatR;

namespace Forge.Api.Features.DomainEvents;

/// <summary>Gated Sequence Engine — every step of a run is Complete/Skipped.</summary>
public record SequenceInstanceCompletedEvent(int InstanceId, int DefinitionId, string? SubjectEntityType, int? SubjectEntityId) : INotification;
