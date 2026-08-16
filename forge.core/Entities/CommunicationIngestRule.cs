using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// One opt-in for mailbox ingestion, by exact address or by domain.
///
/// <para><b>An empty table means ingest nothing.</b> That is the deliberate
/// default: a shop mailbox holds personal mail, invoices, newsletters and
/// recruiter spam, and a feature that vacuums all of it up under the banner of
/// an audit trail is a liability rather than a feature. Ingestion starts closed
/// and each address or domain is added on purpose.</para>
///
/// <para>Free-mail domains are refused for <see cref="IngestRuleMatchType.Domain"/>
/// rules — see <c>FreeMailDomains</c>. A wildcard on gmail.com would attach
/// every unrelated Gmail sender to one customer.</para>
/// </summary>
public class CommunicationIngestRule : BaseAuditableEntity
{
    public IngestRuleMatchType MatchType { get; set; }

    /// <summary>Lowercased. A full mailbox for Address rules, a bare domain ("bobsparts.com") for Domain rules.</summary>
    public string Pattern { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Optional pre-binding. When set, a message matching this rule resolves
    /// straight to this party instead of going through contact lookup — the way
    /// a shop says "anything from bobsparts.com is Bob's Parts" without having
    /// every individual there on file as a Contact.
    /// </summary>
    public CommunicationPartyType? PartyType { get; set; }
    public int? PartyId { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// Confidence a match through this rule earns. Address rules identify one
    /// mailbox and so are Exact; domain rules only prove the sender works
    /// somewhere, which files the mail but never authorizes an order.
    /// </summary>
    public CommunicationMatchConfidence ConfidenceWhenMatched =>
        MatchType == IngestRuleMatchType.Address
            ? CommunicationMatchConfidence.Exact
            : CommunicationMatchConfidence.Domain;
}
