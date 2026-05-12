using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesOrders;

/// <summary>
/// Phase 3 F1 partial / WU-18 — paged sales-order list query, projected from
/// the canonical <see cref="Job"/> entity.
///
/// "Sales order" = a Job whose current stage is <c>order_confirmed</c> or any
/// downstream production stage (materials_ordered, materials_received,
/// in_production, qc_review, shipped, invoiced_sent, payment_received).
///
/// This is a query-side projection only — mutations remain on the legacy
/// <c>/api/v1/orders</c> SalesOrders surface unchanged. Full entity unification
/// is a future architectural pass (F1-broad).
/// </summary>
public record GetSalesOrdersListQuery(SalesOrderListQuery Query)
    : IRequest<PagedResponse<SalesOrderListItemModel>>;

public class GetSalesOrdersListHandler(AppDbContext db)
    : IRequestHandler<GetSalesOrdersListQuery, PagedResponse<SalesOrderListItemModel>>
{
    /// <summary>
    /// Job stage codes that constitute the SO surface. Order_confirmed is the
    /// entry point (per CLAUDE.md production track); everything past it through
    /// payment_received is downstream.
    /// </summary>
    public static readonly string[] SoStageCodes =
    {
        "order_confirmed",
        "materials_ordered",
        "materials_received",
        "in_production",
        "qc_review",
        "shipped",
        "invoiced_sent",
        "payment_received",
    };

    /// <summary>
    /// Map a Job stage code to the SO-status concept. Multiple stages can map to
    /// the same SO-status bucket (e.g. all production stages → InProduction).
    /// </summary>
    public static string MapStageCodeToSoStatus(string? stageCode) => stageCode switch
    {
        "order_confirmed"     => "Confirmed",
        "materials_ordered"   => "InProduction",
        "materials_received"  => "InProduction",
        "in_production"       => "InProduction",
        "qc_review"           => "InProduction",
        "shipped"             => "Shipped",
        "invoiced_sent"       => "Completed",
        "payment_received"    => "Completed",
        _                     => "Unknown",
    };

    /// <summary>
    /// Inverse of <see cref="MapStageCodeToSoStatus"/> — given an SO-status
    /// filter value, return the underlying stage codes that match.
    /// </summary>
    public static string[] SoStatusToStageCodes(string? soStatus) => soStatus switch
    {
        "Confirmed"        => new[] { "order_confirmed" },
        "InProduction"     => new[] { "materials_ordered", "materials_received", "in_production", "qc_review" },
        "Shipped"          => new[] { "shipped" },
        "PartiallyShipped" => new[] { "shipped" },
        "Completed"        => new[] { "invoiced_sent", "payment_received" },
        // Cancelled is a Job disposition, not a stage — surface no rows here.
        "Cancelled"        => Array.Empty<string>(),
        // Empty / unknown → match no rows so the filter behaves as a real filter
        // (vs silently ignoring an unrecognised value, which would mislead the UI).
        _                  => Array.Empty<string>(),
    };

    public async Task<PagedResponse<SalesOrderListItemModel>> Handle(
        GetSalesOrdersListQuery request, CancellationToken cancellationToken)
    {
        var query = request.Query;

        // Base query: Jobs whose current stage code is in the SO surface.
        var q = db.Jobs.AsNoTracking()
            .Where(j => SoStageCodes.Contains(j.CurrentStage.Code));

        // — Filters —
        if (query.CustomerId.HasValue)
            q = q.Where(j => j.CustomerId == query.CustomerId.Value);

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var stageCodes = SoStatusToStageCodes(query.Status);
            // If the status filter resolves to no stage codes, return an empty
            // page (preserves intent of a filter that matched nothing).
            q = q.Where(j => stageCodes.Contains(j.CurrentStage.Code));
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim().ToLower();
            q = q.Where(j =>
                j.JobNumber.ToLower().Contains(term) ||
                j.Title.ToLower().Contains(term) ||
                (j.Customer != null && j.Customer.Name.ToLower().Contains(term)));
        }

        // Date-range filter — DateField selects which Job timestamp.
        // "shipDate" → DueDate (the requested ship/delivery date).
        // anything else (default) → CreatedAt (treated as the order date).
        var useShipDate = string.Equals(query.DateField, "shipDate", StringComparison.OrdinalIgnoreCase);
        if (query.DateFrom.HasValue)
        {
            if (useShipDate)
                q = q.Where(j => j.DueDate >= query.DateFrom.Value);
            else
                q = q.Where(j => j.CreatedAt >= query.DateFrom.Value);
        }
        if (query.DateTo.HasValue)
        {
            if (useShipDate)
                q = q.Where(j => j.DueDate <= query.DateTo.Value);
            else
                q = q.Where(j => j.CreatedAt <= query.DateTo.Value);
        }

        // — Count BEFORE paging —
        var totalCount = await q.CountAsync(cancellationToken);

        // — Sort (whitelist; default = createdAt desc, stable secondary by Id) —
        var sortKey = (query.Sort ?? "").Trim().ToLowerInvariant();
        var desc = query.OrderDescending;
        IOrderedQueryable<Job> ordered = sortKey switch
        {
            "ordernumber"             => desc ? q.OrderByDescending(j => j.JobNumber)        : q.OrderBy(j => j.JobNumber),
            "customername"            => desc ? q.OrderByDescending(j => j.Customer!.Name)   : q.OrderBy(j => j.Customer!.Name),
            "status"                  => desc ? q.OrderByDescending(j => j.CurrentStage.Code): q.OrderBy(j => j.CurrentStage.Code),
            "total"                   => desc ? q.OrderByDescending(j => j.QuotedPrice)      : q.OrderBy(j => j.QuotedPrice),
            "requesteddeliverydate"   => desc ? q.OrderByDescending(j => j.DueDate)          : q.OrderBy(j => j.DueDate),
            "createdat"               => desc ? q.OrderByDescending(j => j.CreatedAt)        : q.OrderBy(j => j.CreatedAt),
            "updatedat"               => desc ? q.OrderByDescending(j => j.UpdatedAt)        : q.OrderBy(j => j.UpdatedAt),
            "id"                      => desc ? q.OrderByDescending(j => j.Id)               : q.OrderBy(j => j.Id),
            _                         => q.OrderByDescending(j => j.CreatedAt),
        };
        ordered = ordered.ThenBy(j => j.Id);

        // — Page slice + projection —
        // Project Job → SalesOrderListItemModel. customerPO has no direct Job
        // analog (Job has no CustomerPO field) so it's null in the projection;
        // lineCount uses the count of JobParts as the closest line analog;
        // total uses Job.QuotedPrice.
        var items = await ordered
            .Skip(query.Skip)
            .Take(query.EffectivePageSize)
            .Select(j => new SalesOrderListItemModel(
                j.Id,
                j.JobNumber,
                j.CustomerId ?? 0,
                j.Customer != null ? j.Customer.Name : string.Empty,
                MapStageCodeToSoStatus(j.CurrentStage.Code),
                null, // CustomerPO — no Job analog (vestigial SO-only field)
                j.JobParts.Count(),
                j.QuotedPrice,
                j.DueDate,
                j.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResponse<SalesOrderListItemModel>(
            items,
            totalCount,
            query.EffectivePage,
            query.EffectivePageSize);
    }
}
