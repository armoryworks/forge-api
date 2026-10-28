namespace Forge.Core.Models;

public record JobStartPlanResponseModel(
    decimal OrderQuantity,
    decimal CalculatedStartQuantity,
    int CalculatedExtraPieces);
