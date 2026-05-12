using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.WorkingCalendars;

/// <summary>
/// Shifts effort — admin updates a calendar-bound shift. Refuses when the
/// target shift isn't bound to the URL's calendar (prevents one calendar
/// from poking another's shift via id-spoofing in the URL path).
/// </summary>
public record UpdateCalendarShiftCommand(int CalendarId, int ShiftId, CalendarShiftRequestModel Body)
    : IRequest<CalendarShiftResponseModel>;

public class UpdateCalendarShiftValidator : AbstractValidator<UpdateCalendarShiftCommand>
{
    public UpdateCalendarShiftValidator()
    {
        RuleFor(x => x.Body.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Body.DaysOfWeekMask).InclusiveBetween(0, 0x7F);
        RuleFor(x => x.Body.PremiumMultiplier).GreaterThanOrEqualTo(0m).LessThanOrEqualTo(10m);
        RuleFor(x => x.Body.CapacityHours).GreaterThanOrEqualTo(0m).LessThanOrEqualTo(24m);
    }
}

public class UpdateCalendarShiftHandler(AppDbContext db)
    : IRequestHandler<UpdateCalendarShiftCommand, CalendarShiftResponseModel>
{
    public async Task<CalendarShiftResponseModel> Handle(UpdateCalendarShiftCommand request, CancellationToken ct)
    {
        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == request.ShiftId, ct)
            ?? throw new KeyNotFoundException($"Shift {request.ShiftId} not found.");

        if (shift.WorkingCalendarId != request.CalendarId)
        {
            throw new KeyNotFoundException(
                $"Shift {request.ShiftId} is not bound to WorkingCalendar {request.CalendarId}.");
        }

        var b = request.Body;
        shift.Name = b.Name.Trim();
        shift.DaysOfWeekMask = b.DaysOfWeekMask;
        shift.StartTime = b.StartTime;
        shift.EndTime = b.EndTime;
        shift.PremiumMultiplier = b.PremiumMultiplier == 0m ? 1.00m : b.PremiumMultiplier;
        shift.CapacityHours = b.CapacityHours;
        shift.IsActive = b.IsActive;

        await db.SaveChangesAsync(ct);

        return new CalendarShiftResponseModel(
            shift.Id, shift.WorkingCalendarId.Value, shift.Name,
            shift.DaysOfWeekMask ?? 0, shift.StartTime, shift.EndTime,
            shift.PremiumMultiplier, shift.CapacityHours,
            EffectiveCapacityHours(shift.CapacityHours, shift.NetHours, shift.StartTime, shift.EndTime),
            shift.IsActive);
    }

    private static decimal EffectiveCapacityHours(decimal capacityHours, decimal netHours, TimeOnly start, TimeOnly end)
    {
        if (capacityHours > 0) return capacityHours;
        if (netHours > 0) return netHours;
        var span = end <= start
            ? (TimeSpan.FromHours(24) - (start - TimeOnly.MinValue) + (end - TimeOnly.MinValue))
            : (end - start);
        return (decimal)span.TotalHours;
    }
}
