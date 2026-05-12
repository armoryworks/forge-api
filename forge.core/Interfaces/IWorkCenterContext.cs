namespace Forge.Core.Interfaces;

// Resolves the work-center + operation that should be frozen on a
// job-related event row (JobActivityLog, StatusEntry) at write time.
//
// The truth we capture is "where was the work physically happening when
// this event occurred" — derived from whichever operator currently has
// an open timer on the job. If no operator is actively timing, we
// honestly return (null, null) instead of guessing from stage metadata.
//
// Used by MoveJobStage, AddHold, ReleaseHold, SetWorkflowStatus, and
// any future handler that creates a job-event row.
public interface IWorkCenterContext
{
    Task<(int? WorkCenterId, int? OperationId)> ResolveForJobAsync(
        int jobId, int? userId, CancellationToken ct);
}
