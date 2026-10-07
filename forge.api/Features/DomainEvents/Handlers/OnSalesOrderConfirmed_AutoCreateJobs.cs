using MediatR;

using Forge.Api.Features.SalesOrders;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnSalesOrderConfirmed_AutoCreateJobs(
    IMediator mediator,
    ILogger<OnSalesOrderConfirmed_AutoCreateJobs> logger)
    : INotificationHandler<SalesOrderConfirmedEvent>
{
    public async Task Handle(SalesOrderConfirmedEvent notification, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateJobsForSalesOrderLinesCommand(notification.SalesOrderId), ct);

        logger.LogInformation("Auto-created {Count} job(s) for confirmed SO {SalesOrderId}; skipped {Skipped} line(s)",
            result.Created, notification.SalesOrderId, result.Skipped.Count);
    }
}
