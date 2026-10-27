using FluentValidation;

using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Scheduling;

public record CreateShiftCommand(
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakMinutes,
    List<ShiftWorkCenterModel>? WorkCenters = null) : IRequest<ShiftResponseModel>;

public class CreateShiftValidator : AbstractValidator<CreateShiftCommand>
{
    public CreateShiftValidator()
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

public class CreateShiftHandler(AppDbContext db) : IRequestHandler<CreateShiftCommand, ShiftResponseModel>
{
    public async Task<ShiftResponseModel> Handle(CreateShiftCommand request, CancellationToken cancellationToken)
    {
        var netHours = ShiftHours.Net(request.StartTime, request.EndTime, request.BreakMinutes);

        var shift = new Shift
        {
            Name = request.Name.Trim(),
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            BreakMinutes = request.BreakMinutes,
            NetHours = netHours,
        };

        db.Shifts.Add(shift);
        if (request.WorkCenters is { Count: > 0 })
            await ShiftWorkCenters.ApplyAsync(db, shift, request.WorkCenters, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        db.LogActivityAt("created", $"Created shift {shift.Name}", ("Shift", shift.Id));
        await db.SaveChangesAsync(cancellationToken);

        var workCenters = await ShiftWorkCenters.LoadAsync(db, [shift.Id], cancellationToken);
        return ShiftWorkCenters.ToResponse(shift, workCenters.GetValueOrDefault(shift.Id));
    }
}
