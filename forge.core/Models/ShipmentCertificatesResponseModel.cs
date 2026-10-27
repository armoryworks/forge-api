namespace Forge.Core.Models;

/// <summary>The certificates now attached to a shipment, and the lots that could not be certified.</summary>
public record ShipmentCertificatesResponseModel(
    List<CertificateOfAnalysisResponseModel> Attached,
    List<CoaAttachWarningModel> Warnings);
