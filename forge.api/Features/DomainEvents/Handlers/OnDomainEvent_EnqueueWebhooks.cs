using MediatR;

using Forge.Api.Features.Webhooks;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnDomainEvent_EnqueueWebhooks(IWebhookEventPublisher webhooks, IClock clock)
    : INotificationHandler<InvoiceCreatedEvent>,
      INotificationHandler<JobCreatedEvent>,
      INotificationHandler<JobStageChangedEvent>,
      INotificationHandler<JobCostOverrunEvent>,
      INotificationHandler<SalesOrderConfirmedEvent>,
      INotificationHandler<ShipmentCreatedEvent>,
      INotificationHandler<ShipmentDeliveredEvent>,
      INotificationHandler<PurchaseOrderCreatedEvent>,
      INotificationHandler<PurchaseOrderReceivedEvent>,
      INotificationHandler<QcInspectionFailedEvent>,
      INotificationHandler<InventoryBelowReorderEvent>
{
    public Task Handle(InvoiceCreatedEvent notification, CancellationToken ct) =>
        webhooks.PublishAsync(WebhookEventTypes.InvoiceCreated, "Invoice", notification.InvoiceId, notification, ct: ct);

    public Task Handle(JobCreatedEvent notification, CancellationToken ct) =>
        webhooks.PublishAsync(WebhookEventTypes.JobCreated, "Job", notification.JobId, notification, ct: ct);

    public Task Handle(JobStageChangedEvent notification, CancellationToken ct) =>
        webhooks.PublishAsync(WebhookEventTypes.JobStageChanged, "Job", notification.JobId, notification,
            $"{notification.FromStageId}-{notification.ToStageId}-{clock.UtcNow.UtcTicks}", ct);

    public Task Handle(JobCostOverrunEvent notification, CancellationToken ct) =>
        webhooks.PublishAsync(WebhookEventTypes.JobCostOverrun, "Job", notification.JobId, notification,
            DayKey(), ct);

    public Task Handle(SalesOrderConfirmedEvent notification, CancellationToken ct) =>
        webhooks.PublishAsync(WebhookEventTypes.SalesOrderConfirmed, "SalesOrder", notification.SalesOrderId, notification, ct: ct);

    public Task Handle(ShipmentCreatedEvent notification, CancellationToken ct) =>
        webhooks.PublishAsync(WebhookEventTypes.ShipmentCreated, "Shipment", notification.ShipmentId, notification, ct: ct);

    public Task Handle(ShipmentDeliveredEvent notification, CancellationToken ct) =>
        webhooks.PublishAsync(WebhookEventTypes.ShipmentDelivered, "Shipment", notification.ShipmentId, notification, ct: ct);

    public Task Handle(PurchaseOrderCreatedEvent notification, CancellationToken ct) =>
        webhooks.PublishAsync(WebhookEventTypes.PurchaseOrderCreated, "PurchaseOrder", notification.PurchaseOrderId, notification, ct: ct);

    public Task Handle(PurchaseOrderReceivedEvent notification, CancellationToken ct) =>
        webhooks.PublishAsync(WebhookEventTypes.PurchaseOrderReceived, "PurchaseOrder", notification.PurchaseOrderId, notification,
            $"{notification.ReceivingRecordId}-{clock.UtcNow.UtcTicks}", ct);

    public Task Handle(QcInspectionFailedEvent notification, CancellationToken ct) =>
        webhooks.PublishAsync(WebhookEventTypes.QcInspectionFailed, "QcInspection", notification.InspectionId, notification, ct: ct);

    public Task Handle(InventoryBelowReorderEvent notification, CancellationToken ct) =>
        webhooks.PublishAsync(WebhookEventTypes.InventoryBelowReorder, "Part", notification.PartId, notification,
            DayKey(), ct);

    private string DayKey() => clock.UtcNow.UtcDateTime.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
}
