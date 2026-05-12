using MediatR;

namespace Forge.Api.Features.DomainEvents;

public record JobStageChangedEvent(int JobId, int FromStageId, int ToStageId, int UserId) : INotification;
