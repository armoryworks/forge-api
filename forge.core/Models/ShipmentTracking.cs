namespace Forge.Core.Models;

public record ShipmentTracking(
    string TrackingNumber,
    string Status,
    DateTimeOffset? EstimatedDelivery,
    List<TrackingEvent> Events);
