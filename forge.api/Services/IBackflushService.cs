namespace Forge.Api.Services;

/// <summary>
/// Consumes a job's Auto-backflush BOM components from stock as the job reports output. Output counts once it is
/// received from a production run or reported on the job's last routing step when that step is complete, whichever
/// is higher, so calling it again after more output consumes only the difference. Material already issued to the
/// job by hand (net of returns) counts toward the requirement, so a component issued manually is not consumed twice.
/// </summary>
public interface IBackflushService
{
    Task BackflushJobAsync(int jobId, int userId, DateTimeOffset now, CancellationToken ct);
}
