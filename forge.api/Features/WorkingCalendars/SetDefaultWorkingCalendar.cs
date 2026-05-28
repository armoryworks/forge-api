using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Data.Context;

namespace Forge.Api.Features.WorkingCalendars;

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

        // Atomic default swap. The filtered unique index (is_default = true) means a
        // single batched "clear old + set new" SaveChanges can violate the constraint:
        // EF does not guarantee it emits the UPDATE that clears the old default before
        // the one that sets the new — if the order flips, two rows momentarily carry
        // is_default = true and Postgres rejects the batch with a 500 (BE-1 / F-12-BE-01).
        // Clear the prior default(s) via a discrete ExecuteUpdate statement first, then
        // set the target, both inside one transaction — so the index only ever sees a
        // single true row at any statement boundary.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.WorkingCalendars
            .Where(c => c.IsDefault && c.Id != request.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDefault, false), ct);

        target.IsDefault = true;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}
