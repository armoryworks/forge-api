namespace Forge.Core.Models;

public record InventoryPartSummaryResponseModel(
    int PartId,
    string PartNumber,
    string Description,
    decimal OnHand,
    decimal Reserved,
    decimal Available,
    List<BinStockResponseModel> BinLocations);
