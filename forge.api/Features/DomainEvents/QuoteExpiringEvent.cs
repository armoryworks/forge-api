using MediatR;

namespace Forge.Api.Features.DomainEvents;

public record QuoteExpiringEvent(int QuoteId, int DaysUntilExpiry, int? AssignedUserId) : INotification;
