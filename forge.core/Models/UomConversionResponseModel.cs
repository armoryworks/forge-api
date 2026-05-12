namespace Forge.Core.Models;

public record UomConversionResponseModel(
    int Id, int FromUomId, string FromUomCode,
    int ToUomId, string ToUomCode,
    decimal ConversionFactor, int? PartId, bool IsReversible);
