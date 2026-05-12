namespace Forge.Core.Models;

public record CreateUomConversionRequestModel(
    int FromUomId, int ToUomId, decimal ConversionFactor,
    int? PartId, bool IsReversible = true);
