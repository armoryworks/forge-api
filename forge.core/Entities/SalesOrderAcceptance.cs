using System.ComponentModel.DataAnnotations;

using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// Proof that a customer accepted a Sales Order's terms. One row per acceptance attempt across every
/// capture channel (manual upload / fax / email / verbal / quote-portal / public accept portal /
/// e-signature / customer external system). The production gate reads only "does an <see
/// cref="AcceptanceStatus.Accepted"/> row exist for this SO" — the <see cref="Method"/> is audit
/// context, not gate logic. Pending rows hold in-flight portal / e-signature requests.
/// </summary>
public class SalesOrderAcceptance : BaseAuditableEntity
{
    public int SalesOrderId { get; set; }

    public AcceptanceStatus Status { get; set; } = AcceptanceStatus.Pending;
    public AcceptanceMethod Method { get; set; }

    /// <summary>Evidence document (signed PDF) when the channel produces one.</summary>
    public int? FileAttachmentId { get; set; }

    /// <summary>Staff member who recorded an offline acceptance (upload / fax / email / verbal).</summary>
    public int? RecordedByUserId { get; set; }

    /// <summary>Customer-side identity captured by portal / e-signature (typed name).</summary>
    [MaxLength(200)]
    public string? AcceptedByName { get; set; }

    /// <summary>Optional link to the accepting customer contact.</summary>
    public int? AcceptedByContactId { get; set; }

    /// <summary>E-signature provider (DocuSeal / DocuSign / …).</summary>
    [MaxLength(50)]
    public string? Provider { get; set; }

    /// <summary>Provider submission id / external system reference.</summary>
    [MaxLength(200)]
    public string? ProviderReference { get; set; }

    /// <summary>Public accept portal: unguessable link token.</summary>
    [MaxLength(128)]
    public string? AccessToken { get; set; }

    /// <summary>Hash of the second key the customer must prove on the public portal.</summary>
    [MaxLength(200)]
    public string? VerificationKeyHash { get; set; }

    /// <summary>Email address a portal / e-signature request was sent to.</summary>
    [MaxLength(320)]
    public string? SentTo { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    /// <summary>Request expiry for portal / e-signature flows.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>When the record became <see cref="AcceptanceStatus.Accepted"/>.</summary>
    public DateTimeOffset? AcceptedAt { get; set; }

    public SalesOrder SalesOrder { get; set; } = null!;
    public FileAttachment? FileAttachment { get; set; }
}
