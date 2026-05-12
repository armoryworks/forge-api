using MediatR;

namespace Forge.Api.Features.DomainEvents;

public record ShipmentCreatedEvent(int ShipmentId, int SalesOrderId, int UserId) : INotification;
