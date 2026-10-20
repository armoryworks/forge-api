using FluentValidation;

using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scheduling;

public record UpdateShiftCommand(
    int Id,
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakMinutes,
    bool IsActive) : IRequest<ShiftResponseModel>;

public class UpdateShiftValidator : AbstractValidator<UpdateShiftCommand>
{
    public UpdateShiftValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BreakMinutes).GreaterThanOrEqualTo(0);
        RuleFor(x => x.BreakMinutes)
            .Must((x, breakMinutes) => ShiftHours.Net(x.StartTime, x.EndTime, breakMinutes) > 0)
            .WithMessage("The break must be shorter than the shift.");
    }
}

public class UpdateShiftHandler(AppDbContext db) : IRequestHandler<UpdateShiftCommand, ShiftResponseModel>
{
    public async Task<ShiftResponseModel> Handle(UpdateShiftCommand request, CancellationToken cancellationToken)
    {
        var shift = await db.Shifts.FindAsync([request.Id], cancellationToken)
            ?? throw new KeyNotFoundException($"Shift {request.Id} not found.");

        shift.Name = request.Name;
        shift.StartTime = request.StartTime;
        shift.EndTime = request.EndTime;
        shift.BreakMinutes = request.BreakMinutes;
        shift.NetHours = ShiftHours.Net(request.StartTime, request.EndTime, request.BreakMinutes);
        shift.IsActive = request.IsActive;

        await db.SaveChangesAsync(cancellationToken);

        return new ShiftResponseModel(
            shift.Id, shift.Name, shift.StartTime, shift.EndTime,
            shift.BreakMinutes, shift.NetHours, shift.IsActive);
    }
}
