
namespace Forge.Core.Entities;

public class EdiTransaction : BaseAuditableEntity
{
    public int TradingPartnerId { get; set; }
    public EdiDirection Direction { get; set; }
    public string TransactionSet { get; set; } = string.Empty;
    public string? ControlNumber { get; set; }
    public string? GroupControlNumber { get; set; }
    public string? TransactionControlNumber { get; set; }

    // Content
    public string RawPayload { get; set; } = string.Empty;
    public string? ParsedDataJson { get; set; }
    public int? PayloadSizeBytes { get; set; }

    // Processing
    public EdiTransactionStatus Status { get; set; } = EdiTransactionStatus.Received;
    public string? RelatedEntityType { get; set; }
    public int? RelatedEntityId { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorDetailJson { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset? LastRetryAt { get; set; }

    // Acknowledgment tracking
    public int? AcknowledgmentTransactionId { get; set; }
    public bool IsAcknowledged { get; set; }

    // Navigation
    public EdiTradingPartner TradingPartner { get; set; } = null!;
    public EdiTransaction? AcknowledgmentTransaction { get; set; }
}
