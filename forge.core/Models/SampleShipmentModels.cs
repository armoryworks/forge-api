namespace Forge.Core.Models;

public record SampleShipmentResponseModel(
    int Id,
    int LeadId,
    int? PartId,
    string? PartDescription,
    int Quantity,
    string Status,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt,
    decimal? CostToUs,
    decimal? ChargedAmount,
    string? TrackingNumber,
    string? Carrier,
    string? Notes,
    DateTimeOffset CreatedAt);

public record CreateSampleShipmentRequest(
    int LeadId,
    int? PartId,
    string? PartDescription,
    int Quantity,
    string? Notes);

public record UpdateSampleShipmentRequest(
    int? PartId,
    string? PartDescription,
    int Quantity,
    string Status,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt,
    decimal? CostToUs,
    decimal? ChargedAmount,
    string? TrackingNumber,
    string? Carrier,
    string? Notes);
