using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>A certificate of analysis with its frozen results (CAP-QC-COA).</summary>
public record CertificateOfAnalysisResponseModel(
    int Id,
    string CertificateNumber,
    int LotRecordId,
    string LotNumber,
    int PartId,
    string PartNumber,
    string? PartRevision,
    int? CustomerId,
    string? CustomerName,
    int? ShipmentId,
    string? ShipmentNumber,
    CertificateOfAnalysisStatus Status,
    bool Conforms,
    string Language,
    DateTimeOffset GeneratedAt,
    string? GeneratedByName,
    int? FileAttachmentId,
    string? Notes,
    List<CoaResultResponseModel> Results);
