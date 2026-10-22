using System.Globalization;

using FluentValidation;

namespace Forge.Api.Validation;

/// <summary>
/// Range and scale rule for a value stored in a <c>numeric(precision, scale)</c> column. Without
/// it a value too large for the column fails the insert with a numeric overflow (a 500) and extra
/// decimals are silently rounded away. Trailing zeros are ignored, so <c>1.50</c> passes at scale 1.
/// </summary>
public static class NumericColumnRules
{
    /// <summary>Fits a <c>numeric(precision, scale)</c> column.</summary>
    public static IRuleBuilderOptions<T, decimal> FitsNumeric<T>(this IRuleBuilder<T, decimal> rule, int precision, int scale)
        => rule.PrecisionScale(precision, scale, true).WithMessage(Message(precision, scale));

    /// <summary>Fits a <c>numeric(precision, scale)</c> column, when a value is supplied.</summary>
    public static IRuleBuilderOptions<T, decimal?> FitsNumeric<T>(this IRuleBuilder<T, decimal?> rule, int precision, int scale)
        => rule.PrecisionScale(precision, scale, true).WithMessage(Message(precision, scale));

    /// <summary>
    /// The largest operation estimate, in milliseconds, whose per-piece minutes still fit the job step's
    /// <c>est_run_minutes_each numeric(14, 6)</c> column.
    /// </summary>
    public static readonly long MaxOperationEstimatedMs = (long)decimal.Floor(MaxValue(14, 6) * 60000m);

    /// <summary>The largest value a <c>numeric(precision, scale)</c> column holds.</summary>
    public static decimal MaxValue(int precision, int scale)
        => Pow10(precision - scale) - 1m / Pow10(scale);

    private static string Message(int precision, int scale)
    {
        var max = MaxValue(precision, scale).ToString("N" + scale, CultureInfo.InvariantCulture);
        return scale == 0
            ? $"{{PropertyName}} must be a whole number no larger than {max}."
            : $"{{PropertyName}} must be no larger than {max} and can have at most {scale} decimal places.";
    }

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++) result *= 10m;
        return result;
    }
}
