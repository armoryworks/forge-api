using System.ComponentModel.DataAnnotations;

namespace Forge.Core.Entities;

/// <summary>
/// One stored version of a <see cref="DocumentSet"/>. Regenerating creates the next version and
/// end-dates + archives the prior one (EffectiveTo set, IsArchived = true). The active version is the
/// one with a null EffectiveTo. The file bytes live in object storage at Bucket/ObjectKey.
/// </summary>
public class DocumentSetVersion : BaseAuditableEntity
{
    public int DocumentSetId { get; set; }
    public DocumentSet DocumentSet { get; set; } = null!;

    public int Version { get; set; }

    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long Size { get; set; }

    [MaxLength(100)]
    public string BucketName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string ObjectKey { get; set; } = string.Empty;

    public DateTimeOffset EffectiveFrom { get; set; }

    /// <summary>Null while this is the current version; set when superseded ("end dated").</summary>
    public DateTimeOffset? EffectiveTo { get; set; }

    public bool IsArchived { get; set; }

    public int? CreatedBy { get; set; }
}
