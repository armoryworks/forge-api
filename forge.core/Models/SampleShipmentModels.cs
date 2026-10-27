namespace Forge.Core.Models;

public record SampleShipmentResponseModel(
    int Id,
    int? LeadId,
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
    DateTimeOffset CreatedAt,
    int? CustomerId = null,
    string? ShipToAddress = null,
    DateTimeOffset? FollowUpDate = null);

public record CreateSampleShipmentRequest(
    int? LeadId,
    int? PartId,
    string? PartDescription,
    int Quantity,
    string? Notes,
    int? CustomerId = null,
    string? ShipToAddress = null,
    string? Carrier = null,
    string? TrackingNumber = null,
    DateTimeOffset? FollowUpDate = null);

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
    string? Notes,
    string? ShipToAddress = null,
    DateTimeOffset? FollowUpDate = null);
