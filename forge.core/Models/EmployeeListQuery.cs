namespace Forge.Core.Models;

/// <summary>
/// Query parameters for <c>GET /api/v1/employees</c>. Phase 3 F7-broad / WU-22 —
/// extends the standard <see cref="PagedQuery"/> with employee-specific
/// filters.
///
/// Backward compat: legacy <c>search</c>, <c>teamId</c>, <c>role</c>, and
/// <c>isActive</c> query params continue to work — the controller plumbs the
/// legacy <c>search</c> into <c>q</c> when <c>q</c> is not supplied.
/// </summary>
public record EmployeeListQuery : PagedQuery
{
    /// <summary>Activation flag. <c>null</c> = both active + inactive.</summary>
    public bool? IsActive { get; init; }

    /// <summary>Restrict to a specific team.</summary>
    public int? TeamId { get; init; }

    /// <summary>Filter by Identity role membership (Admin, Manager, etc.).</summary>
    public string? Role { get; init; }

    /// <summary>Filter by EmployeeProfile.Department.</summary>
    public string? Department { get; init; }
}
