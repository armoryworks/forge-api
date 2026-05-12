namespace Forge.Core.Entities;

/// <summary>
/// 0..1:1 sidecar to <see cref="Contact"/> — same shape as
/// <see cref="LeadOutreachPreferences"/>, just keyed to Contact.Id so
/// suppression survives the lead→customer conversion. Same channel-level
/// opt-outs, same cooldown semantics. The convert-lead handler reads the
/// pre-conversion lead preferences (if any) and creates a parallel row
/// against the new Contact.
///
/// Once a customer relationship exists, this is the authoritative
/// suppression record — bulk-outreach tooling that targets Contacts
/// (re-engagement campaigns, customer-marketing follow-ups) checks
/// here first.
/// </summary>
public class ContactOutreachPreferences : BaseAuditableEntity
{
    public int ContactId { get; set; }

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

    public Contact Contact { get; set; } = null!;
}
