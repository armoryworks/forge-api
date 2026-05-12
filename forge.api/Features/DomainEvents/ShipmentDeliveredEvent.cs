using MediatR;

namespace Forge.Api.Features.DomainEvents;

public record ShipmentDeliveredEvent(int ShipmentId, int SalesOrderId, int UserId) : INotification;
