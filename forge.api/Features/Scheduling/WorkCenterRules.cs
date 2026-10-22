using FluentValidation;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Scheduling;

public interface IWorkCenterFields
{
    string Name { get; }
    string Code { get; }
    decimal DailyCapacityHours { get; }
    decimal EfficiencyPercent { get; }
    int NumberOfMachines { get; }
    decimal LaborCostPerHour { get; }
    decimal BurdenRatePerHour { get; }
    decimal? IdealCycleTimeSeconds { get; }
    BurdenBasis? BurdenBasis { get; }
}

public class WorkCenterFieldsValidator : AbstractValidator<IWorkCenterFields>
{
    public WorkCenterFieldsValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DailyCapacityHours).GreaterThan(0).LessThanOrEqualTo(24)
            .WithMessage("Daily capacity is hours per machine and must be more than 0 and at most 24.");
        RuleFor(x => x.EfficiencyPercent).InclusiveBetween(1, 200);
        RuleFor(x => x.BurdenBasis).IsInEnum().When(x => x.BurdenBasis.HasValue);
        RuleFor(x => x.NumberOfMachines).GreaterThan(0);
        RuleFor(x => x.LaborCostPerHour).GreaterThanOrEqualTo(0);
        RuleFor(x => x.BurdenRatePerHour).GreaterThanOrEqualTo(0);
        RuleFor(x => x.IdealCycleTimeSeconds).GreaterThan(0).When(x => x.IdealCycleTimeSeconds.HasValue);
    }
}

internal static class WorkCenterCodeGuard
{
    public static async Task<string> EnsureUniqueAsync(AppDbContext db, string code, int? excludeId, CancellationToken ct)
    {
        var trimmed = code.Trim();
        var lowered = trimmed.ToLower();
        var taken = await db.WorkCenters
            .IgnoreQueryFilters()
            .AnyAsync(w => w.Id != excludeId && w.Code.ToLower() == lowered, ct);
        if (taken)
            throw new InvalidOperationException($"A work center with code '{trimmed}' already exists.");
        return trimmed;
    }
}
