using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesOrders;

public record CreateBackToBackOrderCommand(int SalesOrderId, int SalesOrderLineId, CreateBackToBackRequestModel Request) : IRequest<int>;

public class CreateBackToBackOrderHandler(AppDbContext db, IBackToBackService backToBackService) : IRequestHandler<CreateBackToBackOrderCommand, int>
{
    public async Task<int> Handle(CreateBackToBackOrderCommand command, CancellationToken cancellationToken)
    {
        var soLine = await db.SalesOrderLines
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == command.SalesOrderLineId && l.SalesOrderId == command.SalesOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order line {command.SalesOrderLineId} not found");

        var po = await backToBackService.CreateBackToBackOrderAsync(command.SalesOrderLineId, command.Request.VendorId, cancellationToken);

        return po.Id;
    }
}
