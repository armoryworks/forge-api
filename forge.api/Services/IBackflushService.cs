namespace Forge.Api.Services;

/// <summary>
/// Consumes a job's Auto-backflush BOM components from stock as the job reports output. Output counts once it is
/// received from a production run or reported on the job's last routing step when that step is complete, whichever
/// is higher, and each component is consumed only as far as the job still owes it: the requirement for that output
/// less everything already issued to the job for the component, by hand or by earlier backflushes, net of returns.
/// An approved alternate issued by hand counts toward its primary component in proportion to the alternate's own
/// requirement, so calling it again after more output consumes only the difference and never double-counts.
/// </summary>
public interface IBackflushService
{
    Task BackflushJobAsync(int jobId, int userId, DateTimeOffset now, CancellationToken ct);
}
