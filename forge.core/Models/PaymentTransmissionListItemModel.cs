namespace Forge.Core.Models;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — list projection of a <c>PaymentTransmission</c> for the finance-ops triage
/// screen. <paramref name="MaxAttempts"/> is the system constant (so the UI can render "3 of 5").
/// </summary>
public record PaymentTransmissionListItemModel(
    int Id,
    string SourceType,
    int SourceId,
    string Status,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? NextAttemptAt,
    string? LastError,
    string? SubmissionRef,
    decimal Amount,
    string Method,
    DateTimeOffset CreatedAt);
