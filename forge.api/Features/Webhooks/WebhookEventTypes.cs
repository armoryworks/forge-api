using System.Text.Json;

namespace Forge.Api.Features.Webhooks;

public static class WebhookEventTypes
{
    public const string Wildcard = "*";
    public const string Ping = "webhook.ping";
    public const string InvoiceCreated = "invoice.created";
    public const string JobCreated = "job.created";
    public const string JobStageChanged = "job.stage_changed";
    public const string JobCostOverrun = "job.cost_overrun";
    public const string SalesOrderConfirmed = "salesorder.confirmed";
    public const string ShipmentCreated = "shipment.created";
    public const string ShipmentDelivered = "shipment.delivered";
    public const string PurchaseOrderCreated = "purchaseorder.created";
    public const string PurchaseOrderReceived = "purchaseorder.received";
    public const string QcInspectionFailed = "qc.inspection_failed";
    public const string InventoryBelowReorder = "inventory.below_reorder";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        InvoiceCreated, JobCreated, JobStageChanged, JobCostOverrun, SalesOrderConfirmed,
        ShipmentCreated, ShipmentDelivered, PurchaseOrderCreated, PurchaseOrderReceived,
        QcInspectionFailed, InventoryBelowReorder,
    };

    public static string[]? Parse(string? eventTypesJson)
    {
        if (string.IsNullOrWhiteSpace(eventTypesJson)) return null;

        try
        {
            return JsonSerializer.Deserialize<string[]>(eventTypesJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsValidList(string? eventTypesJson) =>
        Parse(eventTypesJson) is { Length: > 0 } types
        && types.All(t => t == Wildcard || All.Contains(t));

    public static bool Subscribes(string? eventTypesJson, string eventType) =>
        Parse(eventTypesJson) is { } types
        && types.Any(t => t == Wildcard || string.Equals(t, eventType, StringComparison.OrdinalIgnoreCase));
}
