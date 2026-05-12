using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesOrders;

public record CreateDropShipOrderCommand(int SalesOrderId, int SalesOrderLineId, CreateDropShipRequestModel Request) : IRequest<int>;

public class CreateDropShipOrderHandler(AppDbContext db, IDropShipService dropShipService) : IRequestHandler<CreateDropShipOrderCommand, int>
{
    public async Task<int> Handle(CreateDropShipOrderCommand command, CancellationToken cancellationToken)
    {
        // Verify the sales order line exists
        var soLine = await db.SalesOrderLines
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == command.SalesOrderLineId && l.SalesOrderId == command.SalesOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order line {command.SalesOrderLineId} not found");

        var po = await dropShipService.CreateDropShipPurchaseOrderAsync(command.SalesOrderLineId, command.Request.VendorId, cancellationToken);

        return po.Id;
    }
}
