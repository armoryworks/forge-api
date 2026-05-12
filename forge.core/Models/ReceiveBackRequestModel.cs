namespace Forge.Core.Models;

public record ReceiveBackRequestModel(
    decimal ReceivedQuantity,
    string? ReturnTrackingNumber,
    bool PassedInspection = true,
    string? Notes = null);
