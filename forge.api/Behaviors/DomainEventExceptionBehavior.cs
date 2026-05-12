using System.Text.Json;

using MediatR;

using Forge.Api.Features.DomainEvents;

namespace Forge.Api.Behaviors;

/// <summary>
/// Custom MediatR notification publisher that wraps each handler invocation in try/catch
/// and logs failures to the dead letter queue via DomainEventFailureService.
/// This replaces the default ForeachAwaitPublisher to prevent one handler failure
/// from blocking subsequent handlers.
/// </summary>
public class ResilientNotificationPublisher(IServiceProvider serviceProvider) : INotificationPublisher
{
    public async Task Publish(
        IEnumerable<NotificationHandlerExecutor> handlerExecutors,
        INotification notification,
        CancellationToken cancellationToken)
    {
        foreach (var handler in handlerExecutors)
        {
            try
            {
                await handler.HandlerCallback(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                var eventType = notification.GetType().Name;
                var handlerName = handler.HandlerInstance.GetType().Name;

                var logger = serviceProvider.GetRequiredService<ILogger<ResilientNotificationPublisher>>();
                logger.LogError(ex,
                    "Domain event handler {Handler} failed for {EventType}. Logging to dead letter queue",
                    handlerName, eventType);

                try
                {
                    using var scope = serviceProvider.CreateScope();
                    var failureService = scope.ServiceProvider.GetRequiredService<DomainEventFailureService>();
                    var payload = DomainEventFailureService.SerializeEvent(notification);
                    await failureService.LogFailure(eventType, payload, handlerName, ex.ToString(), cancellationToken);
                }
                catch (Exception logEx)
                {
                    logger.LogCritical(logEx,
                        "Failed to log domain event failure to dead letter queue for {EventType}/{Handler}",
                        eventType, handlerName);
                }
            }
        }
    }
}
