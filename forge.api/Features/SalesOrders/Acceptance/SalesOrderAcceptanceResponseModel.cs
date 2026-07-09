namespace Forge.Api.Features.SalesOrders.Acceptance;

/// <summary>One acceptance record on a Sales Order, for the acceptance panel/history.</summary>
public record SalesOrderAcceptanceResponseModel(
    int Id,
    int SalesOrderId,
    string Status,
    string Method,
    int? FileAttachmentId,
    string? FileName,
    int? RecordedByUserId,
    string? RecordedByName,
    string? AcceptedByName,
    string? Provider,
    string? ProviderReference,
    string? SentTo,
    string? Note,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset CreatedAt);
