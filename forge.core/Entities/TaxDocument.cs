using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class TaxDocument : BaseAuditableEntity
{
    public int UserId { get; set; }
    public TaxDocumentType DocumentType { get; set; }
    public int TaxYear { get; set; }
    public string? EmployerName { get; set; }
    public int? FileAttachmentId { get; set; }
    public PayrollDocumentSource Source { get; set; }
    public string? ExternalId { get; set; }

    public FileAttachment? FileAttachment { get; set; }
}
