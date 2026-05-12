using MediatR;

namespace Forge.Api.Features.DomainEvents;

public record CustomerReturnReceivedEvent(int ReturnId, int SalesOrderId, int UserId) : INotification;
