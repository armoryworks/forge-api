namespace Forge.Core.Models;

/// <summary>
/// Query parameters for <c>GET /api/v1/jobs</c>. Phase 3 F7-broad / WU-22 —
/// extends the standard <see cref="PagedQuery"/> with job-specific filters.
///
/// Governs the jobs table and, with <c>sort=board</c>, the kanban board. The
/// calendar export remains on its own specialised query semantics.
/// <see cref="PagedQuery.Q"/> matches job number, title, part number and
/// customer name.
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

    /// <summary>Restrict to jobs whose current open operation runs at a work center owned by this team.</summary>
    public int? TeamId { get; init; }

    /// <summary>Show archived jobs (default false).</summary>
    public bool IsArchived { get; init; }

    /// <summary>Restrict to jobs that are neither completed nor disposed.</summary>
    public bool ActiveOnly { get; init; }

    /// <summary>Restrict to open jobs whose due date falls before today (UTC).</summary>
    public bool OverdueOnly { get; init; }

    /// <summary>Restrict to jobs with an active hold.</summary>
    public bool OnHoldOnly { get; init; }
}
