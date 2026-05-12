using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.TimeTracking;

public record LockPayPeriodCommand(DateTimeOffset LockThrough) : IRequest<LockPayPeriodResult>;

public record LockPayPeriodResult(int LockedCount);

public class LockPayPeriodHandler(AppDbContext db) : IRequestHandler<LockPayPeriodCommand, LockPayPeriodResult>
{
    public async Task<LockPayPeriodResult> Handle(LockPayPeriodCommand request, CancellationToken ct)
    {
        var lockDate = request.LockThrough.Date;

        var entries = await db.TimeEntries
            .Where(e => e.DeletedAt == null
                && !e.IsLocked
                && e.Date <= DateOnly.FromDateTime(lockDate))
            .ToListAsync(ct);

        foreach (var entry in entries)
            entry.IsLocked = true;

        await db.SaveChangesAsync(ct);

        return new LockPayPeriodResult(entries.Count);
    }
}
