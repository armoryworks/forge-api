using MediatR;

namespace Forge.Api.Features.DomainEvents;

public record SalesOrderCancelledEvent(int SalesOrderId) : INotification;
