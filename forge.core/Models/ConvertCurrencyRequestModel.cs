namespace Forge.Core.Models;

public record ConvertCurrencyRequestModel(
    int FromCurrencyId,
    int ToCurrencyId,
    decimal Amount,
    DateOnly Date);
