using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Data.Services;

public class WorkCenterContext(AppDbContext db) : IWorkCenterContext
{
    public async Task<(int? WorkCenterId, int? OperationId)> ResolveForJobAsync(
        int jobId, int? userId, CancellationToken ct)
    {
        // Prefer the calling user's own active timer — Mike moving a job
        // from his desk shouldn't get tagged with Bob's work center.
        // Fall back to any open timer on the job (multi-shift handoff:
        // Bob clocked out, the job continues, Carol picks it up but the
        // status change happens before her timer starts).
        var baseQuery = db.TimeEntries
            .AsNoTracking()
            .Where(t => t.JobId == jobId
                        && t.TimerStart != null
                        && t.TimerStop == null
                        && t.OperationId != null);

        TimeEntry? activeTimer = null;
        if (userId.HasValue)
        {
            activeTimer = await baseQuery
                .Where(t => t.UserId == userId.Value)
                .OrderByDescending(t => t.TimerStart)
                .FirstOrDefaultAsync(ct);
        }

        activeTimer ??= await baseQuery
            .OrderByDescending(t => t.TimerStart)
            .FirstOrDefaultAsync(ct);

        if (activeTimer is null)
            return (null, null);

        // OperationId is non-null per the WHERE clause above; pull the
        // work center off the operation. If the operation has no work
        // center assigned, we still record the operation — partial truth
        // beats no truth.
        var workCenterId = await db.Operations
            .AsNoTracking()
            .Where(o => o.Id == activeTimer.OperationId!.Value)
            .Select(o => o.WorkCenterId)
            .FirstOrDefaultAsync(ct);

        return (workCenterId, activeTimer.OperationId);
    }
}
