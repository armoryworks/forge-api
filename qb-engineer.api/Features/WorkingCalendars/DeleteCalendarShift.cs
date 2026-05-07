using MediatR;
using Microsoft.EntityFrameworkCore;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.WorkingCalendars;

/// <summary>
/// Shifts effort — soft-delete a calendar-bound shift. Refuses when the
/// shift isn't bound to the URL's calendar (anti id-spoof). Soft-delete
/// preserves the audit trail; existing scheduling/capacity calcs filter
/// out via the global query filter on <c>BaseEntity.DeletedAt</c>.
/// </summary>
public record DeleteCalendarShiftCommand(int CalendarId, int ShiftId) : IRequest;

public class DeleteCalendarShiftHandler(AppDbContext db) : IRequestHandler<DeleteCalendarShiftCommand>
{
    public async Task Handle(DeleteCalendarShiftCommand request, CancellationToken ct)
    {
        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == request.ShiftId, ct)
            ?? throw new KeyNotFoundException($"Shift {request.ShiftId} not found.");

        if (shift.WorkingCalendarId != request.CalendarId)
        {
            throw new KeyNotFoundException(
                $"Shift {request.ShiftId} is not bound to WorkingCalendar {request.CalendarId}.");
        }

        shift.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
