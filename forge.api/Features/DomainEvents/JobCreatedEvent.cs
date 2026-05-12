using MediatR;

namespace Forge.Api.Features.DomainEvents;

public record JobCreatedEvent(int JobId, int UserId) : INotification;
