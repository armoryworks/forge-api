using MediatR;

namespace Forge.Api.Features.DomainEvents;

public record InvoiceOverdueEvent(int InvoiceId, int CustomerId) : INotification;
