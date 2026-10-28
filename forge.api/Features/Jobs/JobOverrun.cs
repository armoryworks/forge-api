using FluentValidation;
using FluentValidation.Results;

using Forge.Core.Entities;

namespace Forge.Api.Features.Jobs;

public static class JobOverrun
{
    public const string LimitCode = "operation-overrun-limit";

    public static decimal TargetQuantity(Job job, decimal buildQuantity) =>
        Math.Min(job.TargetQuantity ?? buildQuantity, buildQuantity);

    public static decimal? Limit(decimal targetQuantity, int? percent) =>
        percent is int p && p >= 0
            ? decimal.Floor(targetQuantity * (100m + p) / 100m * 10_000m) / 10_000m
            : null;

    public static decimal Over(decimal targetQuantity, decimal good) => Math.Max(0m, good - targetQuantity);

    public static string Note(decimal targetQuantity, decimal good) =>
        Over(targetQuantity, good) is var over && over > 0m
            ? $" Overrun: {over:0.####} good over the target quantity of {targetQuantity:0.####}."
            : string.Empty;

    public static ValidationException LimitExceeded(string property, decimal good, decimal targetQuantity, decimal limit) =>
        new([
            new ValidationFailure(property,
                $"Good ({good:0.####}) is more than the overrun allowed on this job: at most {limit:0.####} good "
                    + $"against a target quantity of {targetQuantity:0.####}.")
            {
                ErrorCode = LimitCode,
                FormattedMessagePlaceholderValues = new Dictionary<string, object?> { ["ComparisonValue"] = limit },
            },
        ]);
}
