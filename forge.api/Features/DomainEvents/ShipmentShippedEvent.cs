using MediatR;

namespace Forge.Api.Features.DomainEvents;

public record ShipmentShippedEvent(int ShipmentId, int SalesOrderId, int UserId) : INotification;
