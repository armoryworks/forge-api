using MediatR;

namespace Forge.Api.Features.DomainEvents;

public record InvoicePastDueEvent(int InvoiceId, int CustomerId, int DaysOverdue) : INotification;
