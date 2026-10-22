using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Leave;
using Forge.Api.Features.ShopFloor;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.TimeTracking;

public record LockPayPeriodCommand(DateTimeOffset LockThrough) : IRequest<LockPayPeriodResult>;

public record LockPayPeriodResult(int LockedCount);

public class LockPayPeriodHandler(AppDbContext db, IClock clock) : IRequestHandler<LockPayPeriodCommand, LockPayPeriodResult>
{
    public async Task<LockPayPeriodResult> Handle(LockPayPeriodCommand request, CancellationToken ct)
    {
        var shopZone = await ClockStateRules.ShopTimeZoneAsync(db, ct);
        var lockDate = request.LockThrough.TimeOfDay == TimeSpan.Zero
            ? DateOnly.FromDateTime(request.LockThrough.DateTime)
            : DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(request.LockThrough, shopZone).DateTime);
        var shopToday = DateOnly.FromDateTime(ClockStateRules.LocalToday(shopZone, clock.UtcNow));

        if (lockDate >= shopToday)
            throw new InvalidOperationException(
                $"A pay period can only be locked through a finished day. Choose {shopToday.AddDays(-1):yyyy-MM-dd} or earlier.");

        var running = await db.TimeEntries.AsNoTracking()
            .Where(e => e.DeletedAt == null && e.Date <= lockDate && e.TimerStart != null && e.TimerStop == null)
            .CountAsync(ct);
        if (running > 0)
            throw new InvalidOperationException(running == 1
                ? $"A timer started on or before {lockDate:yyyy-MM-dd} is still running. Stop it before locking the pay period."
                : $"{running} timers started on or before {lockDate:yyyy-MM-dd} are still running. Stop them before locking the pay period.");

        var entries = await db.TimeEntries
            .Where(e => e.DeletedAt == null
                && !e.IsLocked
                && e.Date <= lockDate)
            .ToListAsync(ct);

        foreach (var entry in entries)
            entry.IsLocked = true;

        var boundary = await TimeLockBoundary.AdvanceAsync(db, lockDate, ct);
        await LeaveAccrual.AccrueThroughAsync(db, boundary, ct);

        await db.SaveChangesAsync(ct);

        return new LockPayPeriodResult(entries.Count);
    }
}
