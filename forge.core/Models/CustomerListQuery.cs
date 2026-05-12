namespace Forge.Core.Models;

/// <summary>
/// Query parameters for <c>GET /api/v1/customers</c>. Phase 3 F7-partial /
/// WU-17 standardises pagination, sort, search, and the customer-specific
/// filters around a single bound model.
///
/// Backward compat: callers passing the legacy <c>search</c> / <c>isActive</c>
/// query params continue to work — the controller plumbs both old and new
/// names through to the underlying handler.
/// </summary>
public record CustomerListQuery : PagedQuery
{
    /// <summary>Customer activation flag. <c>null</c> = both active + inactive.</summary>
    public bool? IsActive { get; init; }

    /// <summary>ISO 4217 three-letter currency code filter (e.g. <c>"USD"</c>).</summary>
    public string? DefaultCurrency { get; init; }
}
