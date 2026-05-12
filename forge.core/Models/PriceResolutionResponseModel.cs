namespace Forge.Core.Models;

public record PriceResolutionResponseModel(
    decimal? UnitPrice,
    string? PriceListName,
    string Source);
