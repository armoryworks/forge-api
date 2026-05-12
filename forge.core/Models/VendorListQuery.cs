namespace Forge.Core.Models;

/// <summary>
/// Query parameters for <c>GET /api/v1/vendors</c>. Phase 3 F7-broad / WU-22 —
/// extends the standard <see cref="PagedQuery"/> with vendor-specific filters.
///
/// Backward compat: callers passing the legacy <c>search</c> / <c>isActive</c>
/// query params continue to work — the controller plumbs the legacy
/// <c>search</c> into <c>q</c> when <c>q</c> is not supplied.
/// </summary>
public record VendorListQuery : PagedQuery
{
    /// <summary>Vendor activation flag. <c>null</c> = both active + inactive.</summary>
    public bool? IsActive { get; init; }
}
