using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.WorkingCalendars;

public record UpdateWorkingCalendarCommand(
    int Id,
    string Name,
    string TimeZone,
    int WorkingDaysMask,
    bool IsActive) : IRequest<WorkingCalendarResponseModel>;

public class UpdateWorkingCalendarValidator : AbstractValidator<UpdateWorkingCalendarCommand>
{
    public UpdateWorkingCalendarValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TimeZone).NotEmpty().MaximumLength(64);
        RuleFor(x => x.WorkingDaysMask).InclusiveBetween(1, 127);
    }
}

public class UpdateWorkingCalendarHandler(AppDbContext db)
    : IRequestHandler<UpdateWorkingCalendarCommand, WorkingCalendarResponseModel>
{
    public async Task<WorkingCalendarResponseModel> Handle(UpdateWorkingCalendarCommand request, CancellationToken ct)
    {
        var calendar = await db.WorkingCalendars
            .Include(c => c.Holidays)
            .FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"WorkingCalendar {request.Id} not found.");

        calendar.Name = request.Name.Trim();
        calendar.TimeZone = request.TimeZone.Trim();
        calendar.WorkingDaysMask = request.WorkingDaysMask;
        calendar.IsActive = request.IsActive;

        await db.SaveChangesAsync(ct);

        return new WorkingCalendarResponseModel(
            calendar.Id, calendar.Name, calendar.TimeZone, calendar.WorkingDaysMask,
            calendar.IsDefault, calendar.IsActive,
            calendar.Holidays.OrderBy(h => h.Date).Select(h => new HolidayResponseModel(
                h.Id, h.Date, h.Name, h.ObservedDate, h.IsRecurring)).ToList(),
            calendar.CreatedAt, calendar.UpdatedAt);
    }
}
