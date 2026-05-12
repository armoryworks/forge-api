using Forge.Core.Models.Communications;

namespace Forge.Core.Interfaces.Communications;

/// <summary>
/// Wave 8 — single match implementation shared across every
/// <see cref="ICommunicationSyncProvider"/>. Adapters translate their
/// native event into <see cref="InboundCommunication"/> and hand it here;
/// the matcher normalizes addresses, looks up Lead/Contact, applies
/// tiebreaker logic, and writes the resulting ContactInteraction(s).
///
/// Designed as the single piece of code that knows about address
/// normalization, lead-vs-contact precedence, ambiguity resolution, and
/// the indexing-points activity log. Adding new providers never touches
/// this — only the envelope shape.
/// </summary>
public interface ICommunicationMatcher
{
    /// <summary>Match the inbound communication against active leads
    /// + contacts and create one or more ContactInteraction rows for
    /// successful matches. Returns the result (matched/not, ids,
    /// idempotency external id) so the caller can log telemetry +
    /// dedupe re-deliveries.</summary>
    Task<CommunicationMatchResult> MatchAndLogAsync(InboundCommunication comm, CancellationToken ct);
}
