namespace QBEngineer.Core.Models;

/// <summary>
/// Wire-shape for both <c>LeadOutreachPreferences</c> and
/// <c>ContactOutreachPreferences</c> reads. Returned as null/missing
/// from the API when no preferences row exists for the entity (the
/// 0..1:1 sidecar isn't auto-created — most leads/contacts never
/// need one).
/// </summary>
public record OutreachPreferencesResponseModel(
    int Id,
    int OwnerId,
    bool EmailOptOut,
    DateTimeOffset? EmailOptOutAt,
    string? EmailOptOutSource,
    bool CallOptOut,
    DateTimeOffset? CallOptOutAt,
    string? CallOptOutSource,
    bool SmsOptOut,
    DateTimeOffset? SmsOptOutAt,
    string? SmsOptOutSource,
    DateTimeOffset? CooldownUntil,
    string? CooldownReasonCode,
    string? CooldownNotes);

/// <summary>
/// Upsert payload — partial updates are honored: any field the caller
/// omits (null) leaves the existing value alone. To clear a value (e.g.
/// rescind an opt-out), pass the explicit "false" / empty-string sentinel
/// the field semantics define.
/// </summary>
public record UpdateOutreachPreferencesRequest(
    bool? EmailOptOut,
    string? EmailOptOutSource,
    bool? CallOptOut,
    string? CallOptOutSource,
    bool? SmsOptOut,
    string? SmsOptOutSource,
    DateTimeOffset? CooldownUntil,
    string? CooldownReasonCode,
    string? CooldownNotes);

/// <summary>
/// Compact Lead + suppression-prefs join row for the /leads/suppression
/// list page. Includes the active flags + cooldown so each row's status
/// chips render without a second query per row.
/// </summary>
public record SuppressedLeadSummaryModel(
    int LeadId,
    string CompanyName,
    string? ContactName,
    string? Email,
    string? Phone,
    bool EmailOptOut,
    bool CallOptOut,
    bool SmsOptOut,
    DateTimeOffset? CooldownUntil,
    string? CooldownReasonCode,
    DateTimeOffset PrefsUpdatedAt);
