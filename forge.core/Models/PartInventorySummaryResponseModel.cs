namespace Forge.Core.Models;

public record PartInventorySummaryResponseModel(
    decimal TotalQuantity,
    List<PartBinLocationResponseModel> BinLocations);

public record PartBinLocationResponseModel(
    string LocationPath,
    decimal Quantity);
