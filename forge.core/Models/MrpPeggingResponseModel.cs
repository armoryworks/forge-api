
namespace Forge.Core.Models;

public record MrpPeggingResponseModel(
    int DemandId,
    MrpDemandSource DemandSource,
    int PartId,
    string PartNumber,
    decimal DemandQuantity,
    DateTimeOffset RequiredDate,
    int? SupplyId,
    MrpSupplySource? SupplySource,
    decimal? SupplyQuantity,
    DateTimeOffset? SupplyDate,
    int? PlannedOrderId,
    decimal? PlannedOrderQuantity
);
