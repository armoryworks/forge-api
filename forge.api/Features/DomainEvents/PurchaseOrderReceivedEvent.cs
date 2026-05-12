using MediatR;

namespace Forge.Api.Features.DomainEvents;

public record PurchaseOrderReceivedEvent(int PurchaseOrderId, int ReceivingRecordId, int UserId) : INotification;
