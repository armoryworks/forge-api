namespace Forge.Core.Models;

public record ReceiveBackRequestModel(
    decimal ReceivedQuantity,
    string? ReturnTrackingNumber,
    bool PassedInspection = true,
    string? Notes = null,
    decimal ScrapQuantity = 0,
    bool ConfirmOverTolerance = false,
    string? OverToleranceReason = null,
    bool CloseShort = false);
