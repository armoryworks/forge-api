using MediatR;
using Microsoft.EntityFrameworkCore;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.WorkingCalendars;

public record DeleteHolidayCommand(int CalendarId, int HolidayId) : IRequest<Unit>;

public class DeleteHolidayHandler(AppDbContext db)
    : IRequestHandler<DeleteHolidayCommand, Unit>
{
    public async Task<Unit> Handle(DeleteHolidayCommand request, CancellationToken ct)
    {
        var holiday = await db.Holidays
            .FirstOrDefaultAsync(h => h.Id == request.HolidayId && h.WorkingCalendarId == request.CalendarId, ct)
            ?? throw new KeyNotFoundException($"Holiday {request.HolidayId} not found on calendar {request.CalendarId}.");

        db.Holidays.Remove(holiday);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
