using Forge.Core.Enums;

namespace Forge.Core.Models;

public record ECommerceOrderSyncResponseModel
{
    public int Id { get; init; }
    public string ExternalOrderId { get; init; } = string.Empty;
    public string ExternalOrderNumber { get; init; } = string.Empty;
    public int? SalesOrderId { get; init; }
    public ECommerceOrderSyncStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset ImportedAt { get; init; }
}
