using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// An immutable byte-for-byte copy of something a communication carried — the
/// raw message itself, or one of its attachments.
///
/// <para><b>Why Forge keeps its own copy.</b> The point of the record is to
/// survive the mailbox. If the shop deletes the thread in Gmail, or the mailbox
/// is migrated, or a retention policy sweeps it, the evidence that a customer
/// authorized an order has to still exist here.</para>
///
/// <para><b>Why it derives from <see cref="BaseEntity"/> and not
/// <see cref="BaseAuditableEntity"/>.</b> No <c>DeletedAt</c> means no
/// soft-delete path to accidentally take. Paired with a Postgres
/// BEFORE UPDATE OR DELETE trigger, the same protection the posted GL uses for
/// journal entries. Evidence that can be edited is not evidence.</para>
///
/// <para><see cref="Sha256"/> is computed while the bytes stream to storage, at
/// ingestion, before any parser or extractor sees them. Hashing later would
/// attest to whatever the pipeline had already done to the file.</para>
/// </summary>
public class CommunicationArtifact : BaseEntity
{
    public int CommunicationId { get; set; }
    public Communication Communication { get; set; } = null!;

    public CommunicationArtifactKind Kind { get; set; }

    /// <summary>Lowercase hex SHA-256 of the stored bytes. This is the value rendered on the Authorized-by line.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;
    public long ByteSize { get; set; }

    /// <summary>Filename as the sender supplied it. Untrusted display text — never used to build a storage path.</summary>
    public string? OriginalFilename { get; set; }

    public string BucketName { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;

    /// <summary>When Forge took its copy. Distinct from the message's own send time.</summary>
    public DateTimeOffset IngestedAt { get; set; }

    /// <summary>Short display label: the filename when there is one, otherwise the message subject stand-in.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(OriginalFilename)
            ? (Kind == CommunicationArtifactKind.Message ? "message.eml" : $"artifact-{Id}")
            : OriginalFilename;

    /// <summary>First 12 hex characters, the form shown inline before the reader clicks through.</summary>
    public string ShortHash => Sha256.Length >= 12 ? Sha256[..12] : Sha256;
}
