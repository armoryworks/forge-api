namespace Forge.Core.Models;

/// <summary>
/// Standard query contract for paginated list endpoints. (Phase 3 F7-partial /
/// WU-17.) Entity-specific list query DTOs extend this with their own filter
/// fields (IsActive, Type, dateFrom/dateTo, etc.).
///
/// Defaults: <c>page=1, pageSize=25, sort=null (entity default), order="desc"</c>
/// — backward-compat for callers that don't pass any query params (existing UI
/// callers continue to work).
/// </summary>
public abstract record PagedQuery
{
    /// <summary>1-based page index. Values &lt; 1 are clamped to 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Records per page. Clamped to [1, 200].</summary>
    public int PageSize { get; init; } = 25;

    /// <summary>
    /// Whitelisted sort column. Resolution is per-endpoint to prevent EF
    /// injection — see each handler's <c>ApplySort</c> helper. Null = default
    /// (createdAt desc for transactional lists, name/partNumber asc for
    /// master-data-style lists).
    /// </summary>
    public string? Sort { get; init; }

    /// <summary>"asc" or "desc". Anything else falls back to "asc".</summary>
    public string? Order { get; init; }

    /// <summary>Free-text search across the entity's headline columns.</summary>
    public string? Q { get; init; }

    /// <summary>Inclusive lower bound on createdAt.</summary>
    public DateTimeOffset? DateFrom { get; init; }

    /// <summary>Inclusive upper bound on createdAt.</summary>
    public DateTimeOffset? DateTo { get; init; }

    /// <summary>Returns Page clamped to a minimum of 1.</summary>
    public int EffectivePage => Page < 1 ? 1 : Page;

    /// <summary>Returns PageSize clamped to [1, 200].</summary>
    public int EffectivePageSize => PageSize < 1 ? 25 : (PageSize > 200 ? 200 : PageSize);

    /// <summary>Records to skip (0-based offset). Derived from EffectivePage / EffectivePageSize.</summary>
    public int Skip => (EffectivePage - 1) * EffectivePageSize;

    /// <summary>True iff the order param resolves to descending. Default per-handler.</summary>
    public bool OrderDescending => string.Equals(Order, "desc", StringComparison.OrdinalIgnoreCase);
}
