namespace Forge.Platform.Enums;

/// <summary>
/// Phase 1r / Batch 4 — strategy classification for bulk lead intake.
/// Each strategy drives:
///   • required-field gates (cold-call needs phone; cold-email needs email)
///   • the suppression channel checked during dedup (cold-call rejects rows
///     whose lead/contact has CallOptOut; cold-email checks EmailOptOut)
///   • a default Source string and EngagementShape suggestion
///
/// Defaults are code-driven for v1; promoting to admin-configurable
/// reference data lands in a follow-up if/when shops want custom strategies.
/// </summary>
public enum BulkLeadIntakeStrategy
{
    /// <summary>Phone-first outbound. Requires phone; checks CallOptOut.</summary>
    ColdCall,

    /// <summary>Email-first outbound. Requires email; checks EmailOptOut.</summary>
    ColdEmail,

    /// <summary>Trade-show or event follow-up. Requires email OR phone; checks both opt-outs.</summary>
    TradeShowFollowup,

    /// <summary>Webinar/content attendee. Requires email; checks EmailOptOut.</summary>
    WebinarAttendee,

    /// <summary>Purchased list import. Requires companyName + (email OR phone).</summary>
    ListPurchase,

    /// <summary>Generic — minimum data, no channel-specific suppression check.</summary>
    ManualEntry,
}
