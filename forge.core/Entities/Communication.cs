using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// One exchange with an outside party — an email, a call, a portal action.
///
/// <para>Generalized from <c>ContactInteraction</c>, which could only hang off a
/// Contact. That shape had three costs: vendor correspondence had nowhere to
/// live, mail from an address matching a customer's domain but no named contact
/// was dropped, and nothing could be filed against the Quote or Sales Order it
/// was actually about. All three are what this record exists to fix.</para>
///
/// <para>Channel is deliberately not part of the identity: as the source
/// discussion put it, it does not matter that it is email and it does not
/// matter that it is voice — it is a communication. Adapters translate their
/// native shape into this one so matching, threading and evidence work the same
/// regardless of transport.</para>
/// </summary>
public class Communication : BaseAuditableEntity
{
    /// <summary>
    /// The named person, when known. Nullable since the generalization —
    /// correspondence can be attributable to a customer or vendor without
    /// resolving to an individual.
    /// </summary>
    public int? ContactId { get; set; }

    /// <summary>Which master-data record this belongs to. Null means unmatched and awaiting triage.</summary>
    public CommunicationPartyType? PartyType { get; set; }
    public int? PartyId { get; set; }

    /// <summary>How firmly the party was determined. Only <see cref="CommunicationMatchConfidence.Exact"/> may feed a draft order.</summary>
    public CommunicationMatchConfidence? MatchConfidence { get; set; }

    public CommunicationChannel Channel { get; set; } = CommunicationChannel.Email;
    public CommunicationFlow Flow { get; set; } = CommunicationFlow.Inbound;

    /// <summary>
    /// Groups a back-and-forth into one conversation. For email this is derived
    /// from the RFC 5322 References / In-Reply-To chain, falling back to the
    /// root Message-ID. An audit trail assembled from disconnected fragments is
    /// not an audit trail.
    /// </summary>
    public string? ThreadId { get; set; }

    /// <summary>
    /// The provider's stable id for this event (Gmail messageId, IMAP
    /// UIDVALIDITY+UID, Twilio CallSid). Unique per channel — the idempotency
    /// key that makes a re-delivered webhook or a re-polled mailbox a no-op.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// Sender address or number, normalized (lowercased email / E.164 phone).
    /// Retained even when unmatched so a triage decision has something to act on.
    /// </summary>
    public string? FromAddress { get; set; }

    /// <summary>
    /// Employee who handled this. Null until someone opens an ingested message —
    /// an automatic import has no handler by definition, and claiming one would
    /// put a name against work nobody did.
    /// </summary>
    public int? HandledByUserId { get; set; }

    public InteractionType Type { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? Body { get; set; }

    /// <summary>When the message was sent or the call placed — not when we ingested it.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    public int? DurationMinutes { get; set; }

    /// <summary>True once a human has reviewed an unmatched or domain-matched row.</summary>
    public bool IsTriaged { get; set; }

    public Contact? Contact { get; set; }
    public ICollection<CommunicationArtifact> Artifacts { get; set; } = [];
    public ICollection<CommunicationLink> Links { get; set; } = [];
}
