using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class ECommerceOrderSync : BaseEntity
{
    public int IntegrationId { get; set; }
    public string ExternalOrderId { get; set; } = string.Empty;
    public string ExternalOrderNumber { get; set; } = string.Empty;
    public int? SalesOrderId { get; set; }
    public ECommerceOrderSyncStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public string OrderDataJson { get; set; } = string.Empty;
    public DateTimeOffset ImportedAt { get; set; }

    public ECommerceIntegration Integration { get; set; } = null!;
    public SalesOrder? SalesOrder { get; set; }
}
