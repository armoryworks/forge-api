using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
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
///
/// #25: a Draft <see cref="SalesOrder"/> entity (e.g. fresh from a quote→order
/// convert) has no confirmed Job yet, so the Job projection alone would hide it and
/// users think the order "wasn't created." We therefore also surface Draft entity-SOs,
/// as a LEADING block (pending/actionable orders first) ahead of the Job-projected
/// production rows. Status="Draft" returns only those; a production status returns
/// only Job rows; no status returns drafts-then-jobs.
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

        // #25 — Draft entity-SO block. A Draft SalesOrder has no confirmed Job, so it's
        // absent from the Job projection above; surface it here so a freshly-converted
        // order is visible. Drafts are excluded when the caller filters to a production
        // status (only "Draft" or no status shows them).
        var includeDrafts = string.IsNullOrWhiteSpace(query.Status)
                            || string.Equals(query.Status, "Draft", StringComparison.OrdinalIgnoreCase);

        IQueryable<SalesOrder> draftQuery = db.SalesOrders.AsNoTracking()
            .Where(o => o.Status == SalesOrderStatus.Draft);

        if (query.CustomerId.HasValue)
            draftQuery = draftQuery.Where(o => o.CustomerId == query.CustomerId.Value);
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim().ToLower();
            draftQuery = draftQuery.Where(o =>
                o.OrderNumber.ToLower().Contains(term) ||
                o.Customer.Name.ToLower().Contains(term));
        }
        if (query.DateFrom.HasValue)
            draftQuery = useShipDate
                ? draftQuery.Where(o => o.RequestedDeliveryDate >= query.DateFrom.Value)
                : draftQuery.Where(o => o.CreatedAt >= query.DateFrom.Value);
        if (query.DateTo.HasValue)
            draftQuery = useShipDate
                ? draftQuery.Where(o => o.RequestedDeliveryDate <= query.DateTo.Value)
                : draftQuery.Where(o => o.CreatedAt <= query.DateTo.Value);

        // — Counts BEFORE paging — drafts lead, jobs follow.
        var jobTotal = await q.CountAsync(cancellationToken);
        var draftTotal = includeDrafts ? await draftQuery.CountAsync(cancellationToken) : 0;
        var totalCount = draftTotal + jobTotal;

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

        // Draft block sort mirrors the job keys where there's a direct analog; "total"/
        // "status" fall back to CreatedAt (drafts-first ordering dominates intent anyway).
        IOrderedQueryable<SalesOrder> draftOrdered = sortKey switch
        {
            "ordernumber"             => desc ? draftQuery.OrderByDescending(o => o.OrderNumber)            : draftQuery.OrderBy(o => o.OrderNumber),
            "customername"            => desc ? draftQuery.OrderByDescending(o => o.Customer.Name)          : draftQuery.OrderBy(o => o.Customer.Name),
            "requesteddeliverydate"   => desc ? draftQuery.OrderByDescending(o => o.RequestedDeliveryDate)  : draftQuery.OrderBy(o => o.RequestedDeliveryDate),
            "updatedat"               => desc ? draftQuery.OrderByDescending(o => o.UpdatedAt)              : draftQuery.OrderBy(o => o.UpdatedAt),
            "id"                      => desc ? draftQuery.OrderByDescending(o => o.Id)                     : draftQuery.OrderBy(o => o.Id),
            "createdat"               => desc ? draftQuery.OrderByDescending(o => o.CreatedAt)              : draftQuery.OrderBy(o => o.CreatedAt),
            _                         => draftQuery.OrderByDescending(o => o.CreatedAt),
        };
        draftOrdered = draftOrdered.ThenBy(o => o.Id);

        // — Page slice across the two blocks (drafts lead) —
        var skip = query.Skip;
        var pageSize = query.EffectivePageSize;
        var items = new List<SalesOrderListItemModel>(pageSize);

        // Draft portion of this page (real entity-SO ids → rows are directly openable
        // at /orders/{id}, unlike the Job-projected rows).
        if (includeDrafts && skip < draftTotal)
        {
            var take = Math.Min(pageSize, draftTotal - skip);
            items.AddRange(await draftOrdered
                .Skip(skip)
                .Take(take)
                .Select(o => new SalesOrderListItemModel(
                    o.Id,
                    o.OrderNumber,
                    o.CustomerId,
                    o.Customer.Name,
                    "Draft",
                    o.CustomerPO,
                    o.Lines.Count,
                    o.Lines.Sum(l => l.Quantity * l.UnitPrice),
                    o.RequestedDeliveryDate,
                    o.CreatedAt,
                    o.Id,
                    null))
                .ToListAsync(cancellationToken));
        }

        // Job portion fills the remainder of the page.
        // customerPO has no Job analog (null); lineCount uses JobParts; total uses QuotedPrice.
        // SalesOrderId resolves through SalesOrderLine so the row opens the SO it
        // actually belongs to (Job.Id and SalesOrder.Id are unrelated sequences).
        var remaining = pageSize - items.Count;
        if (remaining > 0)
        {
            var jobSkip = Math.Max(0, skip - draftTotal);
            items.AddRange(await ordered
                .Skip(jobSkip)
                .Take(remaining)
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
                    j.CreatedAt,
                    j.SalesOrderLine != null ? j.SalesOrderLine.SalesOrderId : (int?)null,
                    j.Id))
                .ToListAsync(cancellationToken));
        }

        return new PagedResponse<SalesOrderListItemModel>(
            items,
            totalCount,
            query.EffectivePage,
            query.EffectivePageSize);
    }
}
