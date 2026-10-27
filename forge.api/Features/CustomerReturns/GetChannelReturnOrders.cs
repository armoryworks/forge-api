using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerReturns;

/// <summary>
/// Channel orders a return can be recorded against, newest first, matched on the order number, the
/// channel's own order number or the buyer's name. Freight lines are not returnable and are left out.
/// </summary>
public record GetChannelReturnOrdersQuery(string? Search, int Limit = 25) : IRequest<List<ChannelReturnOrderModel>>;

public class GetChannelReturnOrdersHandler(AppDbContext db) : IRequestHandler<GetChannelReturnOrdersQuery, List<ChannelReturnOrderModel>>
{
    public const int MaxLimit = 50;

    public async Task<List<ChannelReturnOrderModel>> Handle(GetChannelReturnOrdersQuery request, CancellationToken ct)
    {
        var q = db.SalesOrders.AsNoTracking()
            .Where(o => o.ChannelId != null && o.Status != SalesOrderStatus.Cancelled);
        var term = request.Search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var lowered = term.ToLower();
            q = q.Where(o => o.OrderNumber.ToLower().Contains(lowered)
                || (o.ExternalOrderNumber != null && o.ExternalOrderNumber.ToLower().Contains(lowered))
                || (o.RetailBuyer != null && o.RetailBuyer.DisplayName.ToLower().Contains(lowered)));
        }

        return await q
            .OrderByDescending(o => o.CreatedAt)
            .Take(Math.Clamp(request.Limit, 1, MaxLimit))
            .Select(o => new ChannelReturnOrderModel(
                o.Id,
                o.OrderNumber,
                o.ExternalOrderNumber,
                o.ChannelId!.Value,
                o.Channel!.Name,
                o.RetailBuyer != null ? o.RetailBuyer.DisplayName : null,
                o.Customer.Name,
                o.CreatedAt,
                o.Lines
                    .Where(l => l.LineType != DocumentLineType.Freight)
                    .OrderBy(l => l.LineNumber)
                    .Select(l => new ChannelReturnOrderLineModel(
                        l.Id, l.LineNumber, l.Part != null ? l.Part.PartNumber : null, l.Description,
                        l.Quantity, l.ShippedQuantity, l.UnitPrice))
                    .ToList()))
            .ToListAsync(ct);
    }
}
