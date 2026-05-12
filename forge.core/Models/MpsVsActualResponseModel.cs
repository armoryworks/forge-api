namespace Forge.Core.Models;

public record MpsVsActualResponseModel(
    int PartId,
    string PartNumber,
    string? PartDescription,
    decimal PlannedQuantity,
    decimal ActualQuantity,
    decimal Variance,
    decimal VariancePercent
);
