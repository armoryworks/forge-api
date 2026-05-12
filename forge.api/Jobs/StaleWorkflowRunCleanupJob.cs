using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

/// <summary>
/// Auto-abandons entity-less workflow runs (deferred-materialization drafts)
/// that haven't been touched in <see cref="StaleAfterHours"/>. The Drafts
/// section on the parts list pulls from <c>workflow_runs</c> where
/// <c>entity_id IS NULL AND completed_at IS NULL AND abandoned_at IS NULL</c>;
/// without a TTL, every abandoned-via-close-tab wizard accumulates there
/// forever and the section becomes unusable noise.
///
/// <para>Entity-bound runs are intentionally NOT touched here — those are
/// tied to real Part rows the user can still see and act on. If they want
/// to drop one, it's handled via the row-level abandon affordance and the
/// proper handler (which also soft-deletes a Draft entity).</para>
///
/// <para>Runs daily at 4 AM UTC. Idempotent — re-running a second time the
/// same day is a no-op.</para>
/// </summary>
public class StaleWorkflowRunCleanupJob(
    AppDbContext db,
    IClock clock,
    ILogger<StaleWorkflowRunCleanupJob> logger)
{
    private const int StaleAfterHours = 24;

    public async Task CleanupStaleEntitylessRunsAsync(CancellationToken ct = default)
    {
        var cutoff = clock.UtcNow.AddHours(-StaleAfterHours);
        var stale = await db.WorkflowRuns
            .Where(r => r.EntityId == null
                && r.CompletedAt == null
                && r.AbandonedAt == null
                && r.LastActivityAt < cutoff)
            .ToListAsync(ct);

        if (stale.Count == 0)
        {
            logger.LogInformation("StaleWorkflowRunCleanup: no stale entity-less runs older than {Hours}h", StaleAfterHours);
            return;
        }

        var now = clock.UtcNow;
        foreach (var run in stale)
        {
            run.AbandonedAt = now;
            run.AbandonedReason = "stale-cleanup";
            run.LastActivityAt = now;
        }
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "StaleWorkflowRunCleanup: abandoned {Count} entity-less workflow runs older than {Hours}h",
            stale.Count, StaleAfterHours);
    }
}
