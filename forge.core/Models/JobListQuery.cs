namespace Forge.Core.Models;

/// <summary>
/// Query parameters for <c>GET /api/v1/jobs</c>. Phase 3 F7-broad / WU-22 —
/// extends the standard <see cref="PagedQuery"/> with job-specific filters.
///
/// The kanban board (<c>/api/v1/kanban-cards</c>) and calendar export remain
/// on their existing specialised query semantics — this query only governs
/// the table-view list of jobs.
/// </summary>
public record JobListQuery : PagedQuery
{
    /// <summary>Restrict to a specific track type.</summary>
    public int? TrackTypeId { get; init; }

    /// <summary>Restrict to jobs at a specific stage.</summary>
    public int? StageId { get; init; }

    /// <summary>Restrict to jobs assigned to a specific user.</summary>
    public int? AssigneeId { get; init; }

    /// <summary>Restrict to a specific customer.</summary>
    public int? CustomerId { get; init; }

    /// <summary>Show archived jobs (default false).</summary>
    public bool IsArchived { get; init; }
}
