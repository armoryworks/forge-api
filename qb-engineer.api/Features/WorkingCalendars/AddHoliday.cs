using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using QBEngineer.Core.Entities;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.WorkingCalendars;

public record AddHolidayCommand(
    int CalendarId,
    DateOnly Date,
    string Name,
    DateOnly? ObservedDate,
    bool IsRecurring) : IRequest<HolidayResponseModel>;

public class AddHolidayValidator : AbstractValidator<AddHolidayCommand>
{
    public AddHolidayValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public class AddHolidayHandler(AppDbContext db)
    : IRequestHandler<AddHolidayCommand, HolidayResponseModel>
{
    public async Task<HolidayResponseModel> Handle(AddHolidayCommand request, CancellationToken ct)
    {
        var calendarExists = await db.WorkingCalendars.AnyAsync(c => c.Id == request.CalendarId, ct);
        if (!calendarExists)
        {
            throw new KeyNotFoundException($"WorkingCalendar {request.CalendarId} not found.");
        }

        var holiday = new Holiday
        {
            WorkingCalendarId = request.CalendarId,
            Date = request.Date,
            Name = request.Name.Trim(),
            ObservedDate = request.ObservedDate,
            IsRecurring = request.IsRecurring,
        };

        db.Holidays.Add(holiday);
        await db.SaveChangesAsync(ct);

        return new HolidayResponseModel(
            holiday.Id, holiday.Date, holiday.Name, holiday.ObservedDate, holiday.IsRecurring);
    }
}
