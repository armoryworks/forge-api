using System.ComponentModel.DataAnnotations;

using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// A statement a party made, and the evidence for it.
///
/// <para>Generalized from <c>SalesOrderAcceptance</c>, which could only mean
/// "this order was accepted". That covered the case where Forge asks for
/// acceptance, but not the one this exists for: the customer emails their own
/// purchase order, and the authorization is a chain of documents they signed
/// earlier. The master agreement is a statement by the party with no order
/// attached; the PO is a statement about one order that leans on it.</para>
///
/// <para><b>The gate is unchanged.</b> It still asks only whether an
/// <see cref="AcceptanceStatus.Accepted"/> row exists for a given
/// <see cref="SalesOrderId"/>. Party-level rows carry a null SalesOrderId and so
/// never satisfy it, and superseded rows leave <see cref="AcceptanceStatus.Accepted"/>
/// for <see cref="AcceptanceStatus.Superseded"/> — so no consumer needs a new
/// predicate to stay correct.</para>
///
/// <para>Rows are never deleted. An amendment or cancellation writes a new row
/// and points the old one at it through <see cref="SupersededById"/>.</para>
/// </summary>
public class Attestation : BaseAuditableEntity
{
    /// <summary>
    /// The order this statement is about. Null for party-level statements — a
    /// signed MSA authorizes future orders that do not exist yet.
    /// </summary>
    public int? SalesOrderId { get; set; }

    /// <summary>Who made the statement. Required when <see cref="SalesOrderId"/> is null.</summary>
    public CommunicationPartyType? PartyType { get; set; }
    public int? PartyId { get; set; }

    /// <summary>What was stated. Audit context — the gate ignores it.</summary>
    public AttestationStatementType StatementType { get; set; } = AttestationStatementType.OrderAcceptance;

    public AcceptanceStatus Status { get; set; } = AcceptanceStatus.Pending;
    public AcceptanceMethod Method { get; set; }

    /// <summary>
    /// The hashed, immutable copy of the instrument — the .eml, or the PO PDF it
    /// carried. Preferred over <see cref="FileAttachmentId"/> for anything
    /// ingested, because only this one is tamper-evident.
    /// </summary>
    public int? ArtifactId { get; set; }
    public CommunicationArtifact? Artifact { get; set; }

    /// <summary>The communication this arrived through, so the audit line can click back to the original message.</summary>
    public int? CommunicationId { get; set; }
    public Communication? Communication { get; set; }

    /// <summary>
    /// The statement that replaced this one. Set together with
    /// <see cref="AcceptanceStatus.Superseded"/>; the pair is what distinguishes
    /// "the customer amended their PO" from "we entered this in error", which is
    /// <see cref="AcceptanceStatus.Revoked"/> with no pointer.
    /// </summary>
    public int? SupersededById { get; set; }
    public Attestation? SupersededBy { get; set; }

    /// <summary>
    /// A standing agreement this statement leans on — the MSA a purchase order
    /// references. The chain the reviewer follows.
    /// </summary>
    public int? SupportedByAttestationId { get; set; }
    public Attestation? SupportedByAttestation { get; set; }

    /// <summary>
    /// When the party made the statement — the moment the email was sent, not
    /// when staff got to it. <see cref="AcceptedAt"/> is the latter, and for an
    /// emailed PO the two differ by hours.
    /// </summary>
    public DateTimeOffset? CapturedAt { get; set; }

    // ── Pre-existing columns, unchanged ──

    /// <summary>Evidence document (signed PDF) when the channel produces one.</summary>
    public int? FileAttachmentId { get; set; }

    /// <summary>Staff member who recorded an offline acceptance (upload / fax / email / verbal).</summary>
    public int? RecordedByUserId { get; set; }

    [MaxLength(200)]
    public string? AcceptedByName { get; set; }

    public int? AcceptedByContactId { get; set; }

    [MaxLength(50)]
    public string? Provider { get; set; }

    [MaxLength(200)]
    public string? ProviderReference { get; set; }

    [MaxLength(128)]
    public string? AccessToken { get; set; }

    [MaxLength(200)]
    public string? VerificationKeyHash { get; set; }

    [MaxLength(320)]
    public string? SentTo { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    /// <summary>
    /// <see cref="SalesOrderId"/> for the order-scoped code paths that cannot
    /// meaningfully run without one. Named rather than null-forgiving at each
    /// call site so a party-level statement reaching an order-only handler
    /// fails with an explanation instead of a NullReferenceException.
    /// </summary>
    public int RequireSalesOrderId => SalesOrderId
        ?? throw new InvalidOperationException(
            $"Attestation {Id} is a {StatementType} statement scoped to a party, not to an order. "
            + "This code path requires an order-scoped attestation.");

    public SalesOrder? SalesOrder { get; set; }
    public FileAttachment? FileAttachment { get; set; }
}
