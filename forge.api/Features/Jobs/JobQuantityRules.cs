using FluentValidation;

namespace Forge.Api.Features.Jobs;

public static class JobQuantityRules
{
    public const decimal Max = 99_999_999.99m;
    public const string TooLargeMessage = "Quantity can't be more than 99,999,999.99.";
    public const string ScaleMessage = "Quantity can have at most 2 decimal places.";

    public static bool FitsScale(decimal quantity) => decimal.Round(quantity, 2) == quantity;

    public static decimal Fit(decimal quantity) => Math.Min(Max, Math.Ceiling(quantity * 100m) / 100m);

    public static IRuleBuilderOptions<T, decimal> JobQuantityLimits<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.LessThanOrEqualTo(Max).WithMessage(TooLargeMessage)
            .Must(FitsScale).WithMessage(ScaleMessage);

    public static IRuleBuilderOptions<T, decimal?> JobQuantityLimits<T>(this IRuleBuilder<T, decimal?> rule) =>
        rule.LessThanOrEqualTo(Max).WithMessage(TooLargeMessage)
            .Must(q => q is null || FitsScale(q.Value)).WithMessage(ScaleMessage);
}
