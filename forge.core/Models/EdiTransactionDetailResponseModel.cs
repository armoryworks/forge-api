using Forge.Core.Enums;

namespace Forge.Core.Models;

public record EdiTransactionDetailResponseModel
{
    public int Id { get; init; }
    public int TradingPartnerId { get; init; }
    public string TradingPartnerName { get; init; } = string.Empty;
    public EdiDirection Direction { get; init; }
    public string TransactionSet { get; init; } = string.Empty;
    public string? ControlNumber { get; init; }
    public string? GroupControlNumber { get; init; }
    public string? TransactionControlNumber { get; init; }
    public EdiTransactionStatus Status { get; init; }
    public string? RelatedEntityType { get; init; }
    public int? RelatedEntityId { get; init; }
    public DateTimeOffset? ReceivedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorDetailJson { get; init; }
    public int RetryCount { get; init; }
    public DateTimeOffset? LastRetryAt { get; init; }
    public bool IsAcknowledged { get; init; }
    public int? AcknowledgmentTransactionId { get; init; }
    public int? PayloadSizeBytes { get; init; }
    public string RawPayload { get; init; } = string.Empty;
    public string? ParsedDataJson { get; init; }
}
