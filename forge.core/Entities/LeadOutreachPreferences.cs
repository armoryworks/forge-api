namespace Forge.Core.Entities;

/// <summary>
/// 0..1:1 sidecar to <see cref="Lead"/> capturing channel-level opt-outs and
/// time-bounded cooldown windows. Only present when at least one preference
/// is set — most leads never get a row, which keeps the Lead table clean
/// of rarely-used columns.
///
/// At lead → customer conversion, <c>ConvertLeadHandler</c> copies the row
/// forward as a <see cref="ContactOutreachPreferences"/> on the new
/// Contact so suppression survives the boundary.
///
/// Each opt-out flag carries a timestamp + free-text source (e.g.
/// "Reply received 2026-05-10", "DNC requested on call",
/// "Unsubscribed via email link") for audit. Regulatory regimes
/// (TCPA, CAN-SPAM, GDPR) require defensible records of when and how
/// the opt-out was received.
///
/// <see cref="CooldownUntil"/> is the soft "do not contact until X"
/// signal that bulk-outreach tooling honors when assembling queues.
/// Sentinel value: a far-future date (e.g. year 9999) acts as
/// permanent suppression.
/// </summary>
public class LeadOutreachPreferences : BaseAuditableEntity
{
    public int LeadId { get; set; }

    public bool EmailOptOut { get; set; }
    public DateTimeOffset? EmailOptOutAt { get; set; }
    public string? EmailOptOutSource { get; set; }

    public bool CallOptOut { get; set; }
    public DateTimeOffset? CallOptOutAt { get; set; }
    public string? CallOptOutSource { get; set; }

    public bool SmsOptOut { get; set; }
    public DateTimeOffset? SmsOptOutAt { get; set; }
    public string? SmsOptOutSource { get; set; }

    public DateTimeOffset? CooldownUntil { get; set; }
    public string? CooldownReasonCode { get; set; }
    public string? CooldownNotes { get; set; }

    public Lead Lead { get; set; } = null!;
}
