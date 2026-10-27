using FluentValidation;

using Forge.Core.Models;

namespace Forge.Api.Features.Scheduling;

public class ShiftWorkCenterValidator : AbstractValidator<ShiftWorkCenterModel>
{
    public ShiftWorkCenterValidator()
    {
        RuleFor(w => w.WorkCenterId).GreaterThan(0);
        RuleFor(w => w.DaysOfWeek).InclusiveBetween(1, ShiftWorkCenters.AllDays)
            .WithMessage("Pick at least one day for each work center.");
    }

    public static bool AreDistinct(List<ShiftWorkCenterModel>? workCenters) =>
        workCenters is null || workCenters.Select(w => w.WorkCenterId).Distinct().Count() == workCenters.Count;
}
