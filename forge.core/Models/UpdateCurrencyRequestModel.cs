namespace Forge.Core.Models;

public record UpdateCurrencyRequestModel(
    string Code,
    string Name,
    string Symbol,
    int DecimalPlaces,
    bool IsBaseCurrency,
    bool IsActive,
    int SortOrder);
