namespace Forge.Core.Models.Communications;

/// <summary>
/// Wave 8 — outcome of <c>ICommunicationMatcher.MatchAndLogAsync</c>. The
/// caller (sync provider adapter) needs to know whether the message was
/// matched + logged, ambiguously matched + needed a triage decision, or
/// dropped (no lead/contact owns this address). Triage queue + drop
/// telemetry land in subsequent commits.
/// </summary>
public sealed record CommunicationMatchResult(
    /// <summary>True when the matcher created at least one ContactInteraction
    /// row anchored to a Lead or Customer/Contact pair.</summary>
    bool Matched,

    /// <summary>The InteractionId(s) created. Empty when not matched.
    /// Multiple ids when a To list contained more than one address that
    /// each matched a different lead/contact.</summary>
    IReadOnlyList<int> InteractionIds,

    /// <summary>The provider's external id that was processed. Carries the
    /// idempotency key so the caller can dedupe re-deliveries.</summary>
    string ExternalId,

    /// <summary>Why the match failed when <c>Matched</c> is false. Free-form
    /// for now; will narrow to an enum once the triage UX firms up.</summary>
    string? UnmatchedReason = null);
