namespace Forge.Api.Features.Lots;

internal sealed record LotShipmentRow(
    int ShipmentId,
    string ShipmentNumber,
    DateTimeOffset? ShippedDate,
    string? TrackingNumber,
    int CustomerId,
    string CustomerName,
    string? LotNumber,
    decimal Quantity,
    bool IsApproximate);
