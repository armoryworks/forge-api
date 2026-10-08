using System.Linq.Expressions;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesOrders;

/// <summary>
/// Paged sales-order list query over the <see cref="SalesOrder"/> entity. Every
/// status is listed (Draft through Completed and Cancelled), one row per order,
/// with the line count and a total computed from the lines. Work orders are not
/// rows here; they stay on the board.
/// </summary>
public record GetSalesOrdersListQuery(SalesOrderListQuery Query)
    : IRequest<PagedResponse<SalesOrderListItemModel>>;

public class GetSalesOrdersListHandler(AppDbContext db)
    : IRequestHandler<GetSalesOrdersListQuery, PagedResponse<SalesOrderListItemModel>>
{
    /// <summary>
    /// Row projection shared with <see cref="GetSalesOrderProjectionByIdHandler"/>.
    /// Id and SalesOrderId are the order id; JobId is always null.
    /// </summary>
    public static readonly Expression<Func<SalesOrder, SalesOrderListItemModel>> ToListItem = o =>
        new SalesOrderListItemModel(
            o.Id,
            o.OrderNumber,
            o.CustomerId,
            o.Customer.Name,
            o.Status.ToString(),
            o.CustomerPO,
            o.Lines.Count,
            o.Lines.Sum(l => l.Quantity * l.UnitPrice),
            o.RequestedDeliveryDate,
            o.CreatedAt,
            o.Id,
            null);

    /// <summary>
    /// Resolves a status filter value to a <see cref="SalesOrderStatus"/> by name,
    /// ignoring case. Numeric strings and unknown names resolve to null.
    /// </summary>
    public static SalesOrderStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var name = Enum.GetNames<SalesOrderStatus>()
            .FirstOrDefault(n => string.Equals(n, status.Trim(), StringComparison.OrdinalIgnoreCase));
        return name is null ? null : Enum.Parse<SalesOrderStatus>(name);
    }

    public async Task<PagedResponse<SalesOrderListItemModel>> Handle(
        GetSalesOrdersListQuery request, CancellationToken cancellationToken)
    {
        var query = request.Query;

        IQueryable<SalesOrder> q = db.SalesOrders.AsNoTracking();

        if (query.CustomerId.HasValue)
            q = q.Where(o => o.CustomerId == query.CustomerId.Value);

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = ParseStatus(query.Status);
            q = status.HasValue
                ? q.Where(o => o.Status == status.Value)
                : q.Where(o => false);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim().ToLower();
            q = q.Where(o =>
                o.OrderNumber.ToLower().Contains(term) ||
                o.Customer.Name.ToLower().Contains(term) ||
                (o.CustomerPO != null && o.CustomerPO.ToLower().Contains(term)));
        }

        var useShipDate = string.Equals(query.DateField, "shipDate", StringComparison.OrdinalIgnoreCase);
        if (query.DateFrom.HasValue)
            q = useShipDate
                ? q.Where(o => o.RequestedDeliveryDate >= query.DateFrom.Value)
                : q.Where(o => o.CreatedAt >= query.DateFrom.Value);
        if (query.DateTo.HasValue)
            q = useShipDate
                ? q.Where(o => o.RequestedDeliveryDate <= query.DateTo.Value)
                : q.Where(o => o.CreatedAt <= query.DateTo.Value);

        var totalCount = await q.CountAsync(cancellationToken);

        var sortKey = (query.Sort ?? "").Trim().ToLowerInvariant();
        var desc = query.OrderDescending;
        IOrderedQueryable<SalesOrder> ordered = sortKey switch
        {
            "ordernumber"           => desc ? q.OrderByDescending(o => o.OrderNumber)           : q.OrderBy(o => o.OrderNumber),
            "customername"          => desc ? q.OrderByDescending(o => o.Customer.Name)         : q.OrderBy(o => o.Customer.Name),
            "status"                => desc ? q.OrderByDescending(o => o.Status)                : q.OrderBy(o => o.Status),
            "total"                 => desc ? q.OrderByDescending(o => o.Lines.Sum(l => l.Quantity * l.UnitPrice))
                                            : q.OrderBy(o => o.Lines.Sum(l => l.Quantity * l.UnitPrice)),
            "requesteddeliverydate" => desc ? q.OrderByDescending(o => o.RequestedDeliveryDate) : q.OrderBy(o => o.RequestedDeliveryDate),
            "createdat"             => desc ? q.OrderByDescending(o => o.CreatedAt)             : q.OrderBy(o => o.CreatedAt),
            "updatedat"             => desc ? q.OrderByDescending(o => o.UpdatedAt)             : q.OrderBy(o => o.UpdatedAt),
            "id"                    => desc ? q.OrderByDescending(o => o.Id)                    : q.OrderBy(o => o.Id),
            _                       => q.OrderByDescending(o => o.CreatedAt),
        };

        var items = await ordered
            .ThenBy(o => o.Id)
            .Skip(query.Skip)
            .Take(query.EffectivePageSize)
            .Select(ToListItem)
            .ToListAsync(cancellationToken);

        return new PagedResponse<SalesOrderListItemModel>(
            items,
            totalCount,
            query.EffectivePage,
            query.EffectivePageSize);
    }
}
