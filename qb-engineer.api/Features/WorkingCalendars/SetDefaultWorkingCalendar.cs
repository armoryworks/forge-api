using MediatR;
using Microsoft.EntityFrameworkCore;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.WorkingCalendars;

public record SetDefaultWorkingCalendarCommand(int Id) : IRequest<Unit>;

public class SetDefaultWorkingCalendarHandler(AppDbContext db)
    : IRequestHandler<SetDefaultWorkingCalendarCommand, Unit>
{
    public async Task<Unit> Handle(SetDefaultWorkingCalendarCommand request, CancellationToken ct)
    {
        var target = await db.WorkingCalendars
            .FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"WorkingCalendar {request.Id} not found.");

        if (!target.IsActive)
        {
            throw new InvalidOperationException("Cannot set an inactive calendar as default.");
        }

        // Clear the previous default(s). Filtered unique index enforces
        // single-default at the DB level — without this, the SaveChanges
        // would fail. Same pattern as CompanyLocation.IsDefault.
        var existing = await db.WorkingCalendars
            .Where(c => c.IsDefault && c.Id != request.Id)
            .ToListAsync(ct);
        foreach (var c in existing) c.IsDefault = false;

        target.IsDefault = true;
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
