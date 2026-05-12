using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class IdentityDocument : BaseAuditableEntity
{
    public int UserId { get; set; }
    public IdentityDocumentType DocumentType { get; set; }
    public int FileAttachmentId { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public int? VerifiedById { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? Notes { get; set; }

    // Wizard-captured detail (List A/B/C). DocumentName + IssuingAuthority are
    // non-sensitive descriptors ("U.S. Passport", "U.S. Department of State"
    // etc.). DocumentNumberProtected holds DP ciphertext — never project as
    // plaintext to clients.
    public string? DocumentName { get; set; }
    public string? IssuingAuthority { get; set; }
    public string? DocumentNumberProtected { get; set; }

    public FileAttachment FileAttachment { get; set; } = null!;
}
