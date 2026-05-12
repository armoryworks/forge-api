namespace Forge.Core.Models;

/// <summary>
/// Standard envelope for paginated list endpoints. (Phase 3 F7-partial / WU-17.)
///
/// Phase 1C found list endpoints universally ignored page / pageSize / sort /
/// order / q / filter query params. WU-17 standardises the envelope on a
/// representative subset (customers + parts); WU-22 sweeps the rest.
///
/// Shape per WU-17 charter: <c>{ items, totalCount, page, pageSize }</c>.
/// CLAUDE.md (lines 540-545) documents <c>data</c> instead of <c>items</c>;
/// WU-17 follows the work-unit charter so this shape is the canonical one
/// going forward.
/// </summary>
public record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize);
