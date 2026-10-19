using FluentValidation;

using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scheduling;

public record CreateShiftCommand(
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakMinutes) : IRequest<ShiftResponseModel>;

public class CreateShiftValidator : AbstractValidator<CreateShiftCommand>
{
    public CreateShiftValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BreakMinutes).GreaterThanOrEqualTo(0);
    }
}

public class CreateShiftHandler(AppDbContext db) : IRequestHandler<CreateShiftCommand, ShiftResponseModel>
{
    public async Task<ShiftResponseModel> Handle(CreateShiftCommand request, CancellationToken cancellationToken)
    {
        var netHours = ShiftHours.Net(request.StartTime, request.EndTime, request.BreakMinutes);

        var shift = new Shift
        {
            Name = request.Name,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            BreakMinutes = request.BreakMinutes,
            NetHours = netHours,
        };

        db.Shifts.Add(shift);
        await db.SaveChangesAsync(cancellationToken);

        return new ShiftResponseModel(
            shift.Id, shift.Name, shift.StartTime, shift.EndTime,
            shift.BreakMinutes, shift.NetHours, shift.IsActive);
    }
}
