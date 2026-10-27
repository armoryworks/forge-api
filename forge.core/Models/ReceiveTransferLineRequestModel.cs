namespace Forge.Core.Models;

public record ReceiveTransferLineRequestModel(
    int PartId,
    decimal ReceivedQuantity,
    int? ToLocationId = null,
    int? LineId = null);
