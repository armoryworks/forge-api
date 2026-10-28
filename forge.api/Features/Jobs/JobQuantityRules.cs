using FluentValidation;

namespace Forge.Api.Features.Jobs;

public static class JobQuantityRules
{
    public const decimal Max = 999_999_999m;
    public const string TooLargeMessage = "Quantity can't be more than 999,999,999.";
    public const string ScaleMessage = "Quantity can have at most 4 decimal places.";

    public static bool FitsScale(decimal quantity) => decimal.Round(quantity, 4) == quantity;

    public static decimal Fit(decimal quantity) => Math.Min(Max, Math.Ceiling(quantity * 10_000m) / 10_000m);

    public static IRuleBuilderOptions<T, decimal> JobQuantityLimits<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.LessThanOrEqualTo(Max).WithMessage(TooLargeMessage)
            .Must(FitsScale).WithMessage(ScaleMessage);

    public const string ExtraPiecesMessage = "Extra pieces must be a whole number from 0 to 999,999,999.";

    public static IRuleBuilderOptions<T, int?> ExtraPiecesLimits<T>(this IRuleBuilder<T, int?> rule) =>
        rule.Must(e => e is null || (e.Value >= 0 && e.Value <= Max)).WithMessage(ExtraPiecesMessage);

    public static IRuleBuilderOptions<T, decimal?> JobQuantityLimits<T>(this IRuleBuilder<T, decimal?> rule) =>
        rule.LessThanOrEqualTo(Max).WithMessage(TooLargeMessage)
            .Must(q => q is null || FitsScale(q.Value)).WithMessage(ScaleMessage);
}
