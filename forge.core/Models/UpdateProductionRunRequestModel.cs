namespace Forge.Core.Models;

public record UpdateProductionRunRequestModel(
    int CompletedQuantity,
    int ScrapQuantity,
    string Status,
    string? Notes,
    decimal? SetupTimeMinutes,
    decimal? RunTimeMinutes);
