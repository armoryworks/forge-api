using MediatR;

namespace Forge.Api.Features.DomainEvents;

public record PurchaseOrderCreatedEvent(int PurchaseOrderId, int UserId) : INotification;
