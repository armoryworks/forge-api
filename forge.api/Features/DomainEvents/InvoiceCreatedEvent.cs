using MediatR;

namespace Forge.Api.Features.DomainEvents;

public record InvoiceCreatedEvent(int InvoiceId, string InvoiceNumber, int CustomerId, decimal Total) : INotification;
