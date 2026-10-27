using FluentValidation;

using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Scheduling;

public record UpdateShiftCommand(
    int Id,
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakMinutes,
    bool IsActive,
    List<ShiftWorkCenterModel>? WorkCenters = null) : IRequest<ShiftResponseModel>;

public class UpdateShiftValidator : AbstractValidator<UpdateShiftCommand>
{
    public UpdateShiftValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BreakMinutes).GreaterThanOrEqualTo(0);
        RuleFor(x => x.BreakMinutes)
            .Must((x, breakMinutes) => ShiftHours.Net(x.StartTime, x.EndTime, breakMinutes) > 0)
            .WithMessage("The break must be shorter than the shift.");
        RuleForEach(x => x.WorkCenters).SetValidator(new ShiftWorkCenterValidator());
        RuleFor(x => x.WorkCenters).Must(ShiftWorkCenterValidator.AreDistinct)
            .WithMessage("Each work center can be listed once.");
    }
}

public class UpdateShiftHandler(AppDbContext db) : IRequestHandler<UpdateShiftCommand, ShiftResponseModel>
{
    public async Task<ShiftResponseModel> Handle(UpdateShiftCommand request, CancellationToken cancellationToken)
    {
        var shift = await db.Shifts.FindAsync([request.Id], cancellationToken)
            ?? throw new KeyNotFoundException($"Shift {request.Id} not found.");

        shift.Name = request.Name.Trim();
        shift.StartTime = request.StartTime;
        shift.EndTime = request.EndTime;
        shift.BreakMinutes = request.BreakMinutes;
        shift.NetHours = ShiftHours.Net(request.StartTime, request.EndTime, request.BreakMinutes);
        shift.IsActive = request.IsActive;

        db.ChangeTracker.DetectChanges();
        var changed = ActivityChangeSummary.ChangedProperties(db.Entry(shift)).ToList();
        if (request.WorkCenters is not null)
            changed.AddRange(await ShiftWorkCenters.ApplyAsync(db, shift, request.WorkCenters, cancellationToken));

        if (ActivityChangeSummary.Updated(changed) is string summary)
            db.LogActivityAt("updated", $"{summary} on shift {shift.Name}", ("Shift", shift.Id));

        await db.SaveChangesAsync(cancellationToken);

        var workCenters = await ShiftWorkCenters.LoadAsync(db, [shift.Id], cancellationToken);
        return ShiftWorkCenters.ToResponse(shift, workCenters.GetValueOrDefault(shift.Id));
    }
}
