using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Data.Context;

namespace Forge.Api.Features.WorkingCalendars;

public record DeleteWorkingCalendarCommand(int Id) : IRequest<Unit>;

public class DeleteWorkingCalendarHandler(AppDbContext db)
    : IRequestHandler<DeleteWorkingCalendarCommand, Unit>
{
    public async Task<Unit> Handle(DeleteWorkingCalendarCommand request, CancellationToken ct)
    {
        var calendar = await db.WorkingCalendars
            .FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"WorkingCalendar {request.Id} not found.");

        if (calendar.IsDefault)
        {
            throw new InvalidOperationException(
                "Cannot delete the default calendar. Mark another calendar as default first.");
        }

        // Check for FK references — locations using this calendar.
        var inUse = await db.CompanyLocations.AnyAsync(l => l.WorkingCalendarId == request.Id, ct);
        if (inUse)
        {
            throw new InvalidOperationException(
                "Cannot delete a calendar that's assigned to one or more locations. Reassign those locations first.");
        }

        db.WorkingCalendars.Remove(calendar);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
