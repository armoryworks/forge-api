using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesOrders;

/// <summary>
/// Single-row sales-order projection in the list shape. The id is the
/// <see cref="Forge.Core.Entities.SalesOrder"/> id; returns null when no such
/// order exists. Mirrors <see cref="GetSalesOrdersListHandler"/>.
/// </summary>
public record GetSalesOrderProjectionByIdQuery(int Id)
    : IRequest<SalesOrderListItemModel?>;

public class GetSalesOrderProjectionByIdHandler(AppDbContext db)
    : IRequestHandler<GetSalesOrderProjectionByIdQuery, SalesOrderListItemModel?>
{
    public async Task<SalesOrderListItemModel?> Handle(
        GetSalesOrderProjectionByIdQuery request, CancellationToken cancellationToken)
    {
        return await db.SalesOrders.AsNoTracking()
            .Where(o => o.Id == request.Id)
            .Select(GetSalesOrdersListHandler.ToListItem)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
