using MediatR;

namespace Forge.Api.Features.DomainEvents;

public record SalesOrderConfirmedEvent(int SalesOrderId, int UserId) : INotification;
