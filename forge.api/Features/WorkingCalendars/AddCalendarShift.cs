using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.WorkingCalendars;

/// <summary>
/// Shifts effort — admin adds a calendar-bound shift. The shift's
/// <c>WorkingCalendarId</c> is set, distinguishing it from work-center
/// shift templates (which have it null and live alongside in the same
/// table). Drives hours-of-operation, MRP capacity, and (future)
/// payroll premium application.
/// </summary>
public record AddCalendarShiftCommand(int CalendarId, CalendarShiftRequestModel Body)
    : IRequest<CalendarShiftResponseModel>;

public class AddCalendarShiftValidator : AbstractValidator<AddCalendarShiftCommand>
{
    public AddCalendarShiftValidator()
    {
        RuleFor(x => x.Body.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Body.DaysOfWeekMask).InclusiveBetween(0, 0x7F)
            .WithMessage("DaysOfWeekMask must be a 7-bit value (Sun..Sat).");
        RuleFor(x => x.Body.PremiumMultiplier).GreaterThanOrEqualTo(0m).LessThanOrEqualTo(10m);
        RuleFor(x => x.Body.CapacityHours).GreaterThanOrEqualTo(0m).LessThanOrEqualTo(24m);
    }
}

public class AddCalendarShiftHandler(AppDbContext db)
    : IRequestHandler<AddCalendarShiftCommand, CalendarShiftResponseModel>
{
    public async Task<CalendarShiftResponseModel> Handle(AddCalendarShiftCommand request, CancellationToken ct)
    {
        var calendarExists = await db.WorkingCalendars.AnyAsync(c => c.Id == request.CalendarId, ct);
        if (!calendarExists)
        {
            throw new KeyNotFoundException($"WorkingCalendar {request.CalendarId} not found.");
        }

        var b = request.Body;
        var shift = new Shift
        {
            WorkingCalendarId = request.CalendarId,
            Name = b.Name.Trim(),
            DaysOfWeekMask = b.DaysOfWeekMask,
            StartTime = b.StartTime,
            EndTime = b.EndTime,
            PremiumMultiplier = b.PremiumMultiplier == 0m ? 1.00m : b.PremiumMultiplier,
            CapacityHours = b.CapacityHours,
            IsActive = b.IsActive,
        };

        db.Shifts.Add(shift);
        await db.SaveChangesAsync(ct);

        return new CalendarShiftResponseModel(
            shift.Id, shift.WorkingCalendarId!.Value, shift.Name,
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
