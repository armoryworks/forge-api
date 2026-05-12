namespace Forge.Core.Models;

public record CreateCurrencyRequestModel(
    string Code,
    string Name,
    string Symbol,
    int DecimalPlaces,
    bool IsBaseCurrency,
    int SortOrder);
