namespace Forge.Core.Models;

public record ScanReceiveContextLine(
    int PurchaseOrderLineId,
    string PoNumber,
    decimal ExpectedQuantity,
    decimal ReceivedQuantity,
    decimal RemainingQuantity,
    bool LotTracked = false);
