namespace Forge.Core.Models;

public record PartInventorySummaryResponseModel(
    decimal TotalQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    List<PartBinLocationResponseModel> BinLocations);
