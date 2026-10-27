namespace Forge.Core.Models;

/// <summary>Generates a certificate of analysis for a lot from its QC inspection and SPC results; the customer and shipment are optional.</summary>
public record GenerateCertificateOfAnalysisRequestModel(
    int LotId,
    int? CustomerId = null,
    int? ShipmentId = null,
    string? Language = null,
    string? Notes = null);
