using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// CAP-QC-COA — a certificate of analysis for one lot: the frozen inspection and SPC results for that lot
/// against their specification limits, optionally issued for a customer and attached to the shipment that
/// carried the lot. The PDF is rendered from this snapshot, so a reprint always shows what was certified.
/// </summary>
public class CertificateOfAnalysis : BaseAuditableEntity
{
    public string CertificateNumber { get; set; } = string.Empty;
    public int LotRecordId { get; set; }
    public int PartId { get; set; }
    public int? CustomerId { get; set; }
    public int? ShipmentId { get; set; }
    public CertificateOfAnalysisStatus Status { get; set; } = CertificateOfAnalysisStatus.Issued;

    /// <summary>True when every result on the certificate passed its specification.</summary>
    public bool Conforms { get; set; } = true;

    /// <summary>The language the archived PDF was printed in ("en" or "es").</summary>
    public string Language { get; set; } = "en";
    public DateTimeOffset GeneratedAt { get; set; }
    public int? GeneratedById { get; set; }

    /// <summary>The archived PDF as issued, stored with the lot's files.</summary>
    public int? FileAttachmentId { get; set; }
    public string? Notes { get; set; }

    public LotRecord LotRecord { get; set; } = null!;
    public Part Part { get; set; } = null!;
    public Customer? Customer { get; set; }
    public Shipment? Shipment { get; set; }
    public FileAttachment? FileAttachment { get; set; }
    public ICollection<CoaResult> Results { get; set; } = [];
}
