namespace Forge.Core.Enums;

/// <summary>
/// Phase 1r / Batch 5 — orthogonal to <see cref="LeadStatus"/>. The
/// existing funnel statuses (New → Contacted → Qualified → Proposal →
/// Won/Lost) describe sales-funnel position. High-volume outreach work
/// needs separate substates for *attempts* against a lead:
///
///   • Queued — sitting in the worker queue waiting for a rep
///   • InProgress — a rep has the lead checked out
///   • NoAnswer — call placed, voicemail not left or rang out
///   • VoicemailLeft — left a message; ready to retry per cadence
///   • CallbackScheduled — prospect wants a specific time
///   • BadData — wrong number / disconnected / bounce
///   • Engaged — meaningful contact made; lead exits outreach and
///     enters the sales funnel proper (status moves to Contacted /
///     Qualified)
///   • Suppressed — opt-out or cooldown blocks further attempts
///
/// A single Lead can iterate through multiple OutreachStates within a
/// single LeadStatus (Queued → NoAnswer → VoicemailLeft → Engaged
/// while Status stays New). Decoupling them avoids overloading the
/// funnel enum with attempt-level transitions.
/// </summary>
public enum OutreachState
{
    Queued,
    InProgress,
    NoAnswer,
    VoicemailLeft,
    CallbackScheduled,
    BadData,
    Engaged,
    Suppressed,
}
