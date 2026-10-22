using FluentValidation;

using Forge.Core.Models.Accounting;

namespace Forge.Api.Validation;

/// <summary>
/// Scale rules matching the money and unit column scales: an amount of money carries at most
/// <see cref="Money.Scale"/> decimals (numeric(18,2)), a unit price, unit cost or quantity at most
/// <see cref="Money.UnitScale"/> (numeric(18,4)). Without them the column silently rounds the
/// extra digits away. Trailing zeros are ignored, so <c>10.500</c> passes as a 2 dp amount.
/// </summary>
public static class AmountPrecisionRules
{
    private const int Precision = 18;

    /// <summary>At most 2 decimal places.</summary>
    public static IRuleBuilderOptions<T, decimal> MoneyScale<T>(this IRuleBuilder<T, decimal> rule)
        => rule.PrecisionScale(Precision, Money.Scale, true).WithMessage(MoneyMessage);

    /// <summary>At most 2 decimal places, when a value is supplied.</summary>
    public static IRuleBuilderOptions<T, decimal?> MoneyScale<T>(this IRuleBuilder<T, decimal?> rule)
        => rule.PrecisionScale(Precision, Money.Scale, true).WithMessage(MoneyMessage);

    /// <summary>At most 4 decimal places.</summary>
    public static IRuleBuilderOptions<T, decimal> UnitScale<T>(this IRuleBuilder<T, decimal> rule)
        => rule.PrecisionScale(Precision, Money.UnitScale, true).WithMessage(UnitMessage);

    /// <summary>At most 4 decimal places, when a value is supplied.</summary>
    public static IRuleBuilderOptions<T, decimal?> UnitScale<T>(this IRuleBuilder<T, decimal?> rule)
        => rule.PrecisionScale(Precision, Money.UnitScale, true).WithMessage(UnitMessage);

    private const string MoneyMessage = "{PropertyName} can have at most 2 decimal places.";
    private const string UnitMessage = "{PropertyName} can have at most 4 decimal places.";
}
