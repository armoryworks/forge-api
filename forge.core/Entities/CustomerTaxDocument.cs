using System.ComponentModel.DataAnnotations;

using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// A customer's state tax certificate (resale/exemption) with its verification
/// workflow. References the generic FileAttachment for the underlying file;
/// verification state is tax-domain data, so it lives here, not on the file.
/// A Verified, unexpired document is what unlocks editing a quote's tax rate.
/// </summary>
public class CustomerTaxDocument : BaseAuditableEntity
{
    public int CustomerId { get; set; }
    public int FileAttachmentId { get; set; }
    [MaxLength(2)]
    public string? StateCode { get; set; }
    [MaxLength(30)]
    public string CertificateType { get; set; } = "Exemption";
    [MaxLength(100)]
    public string? CertificateNumber { get; set; }
    public TaxDocumentStatus Status { get; set; } = TaxDocumentStatus.Pending;
    public int? VerifiedById { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    [MaxLength(500)]
    public string? RejectionReason { get; set; }
    public DateTimeOffset? ExpirationDate { get; set; }
    public string? ParsedFields { get; set; }
    [MaxLength(50)]
    public string? SignatureProvider { get; set; }
    [MaxLength(200)]
    public string? SignatureRef { get; set; }

    public Customer Customer { get; set; } = null!;
    public FileAttachment FileAttachment { get; set; } = null!;
}
