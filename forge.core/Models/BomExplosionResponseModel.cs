namespace Forge.Core.Models;

public record BomExplosionResponseModel(
    int ParentJobId,
    List<BomExplosionChildJobModel> CreatedJobs,
    List<BomExplosionBuyItemModel> BuyItems,
    List<BomExplosionStockItemModel> StockItems);
