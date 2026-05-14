
namespace Forge.Core.Models;

public record EdiTransactionResponseModel
{
    public int Id { get; init; }
    public int TradingPartnerId { get; init; }
    public string TradingPartnerName { get; init; } = string.Empty;
    public EdiDirection Direction { get; init; }
    public string TransactionSet { get; init; } = string.Empty;
    public string? ControlNumber { get; init; }
    public EdiTransactionStatus Status { get; init; }
    public string? RelatedEntityType { get; init; }
    public int? RelatedEntityId { get; init; }
    public DateTimeOffset? ReceivedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }
    public string? ErrorMessage { get; init; }
    public int RetryCount { get; init; }
    public bool IsAcknowledged { get; init; }
    public int? PayloadSizeBytes { get; init; }
}
