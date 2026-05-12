namespace Forge.Core.Models;

public record CurrencyResponseModel(
    int Id,
    string Code,
    string Name,
    string Symbol,
    int DecimalPlaces,
    bool IsBaseCurrency,
    bool IsActive,
    int SortOrder);
