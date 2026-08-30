namespace Forge.Core.Models;

/// <summary>
/// One decision in the consent history. Kept for both answers, not just the yes:
/// being able to show that monitoring was offered and declined on a given date is as
/// much a part of the record as an acceptance.
/// </summary>
public sealed record TelemetryConsentRecordResponseModel(
    DateTimeOffset At,
    string Decision,
    string Version,
    string? By,
    string? IpAddress);
