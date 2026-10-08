using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PurchaseOrders;

public record DeletePurchaseOrderLineCommand(int PurchaseOrderId, int LineId)
    : IRequest<PurchaseOrderDetailResponseModel>;

public class DeletePurchaseOrderLineHandler(
    IPurchaseOrderRepository repo,
    AppDbContext db,
    IMediator mediator)
    : IRequestHandler<DeletePurchaseOrderLineCommand, PurchaseOrderDetailResponseModel>
{
    public async Task<PurchaseOrderDetailResponseModel> Handle(DeletePurchaseOrderLineCommand request, CancellationToken cancellationToken)
    {
        var po = await repo.FindWithDetailsAsync(request.PurchaseOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order {request.PurchaseOrderId} not found");

        if (po.Status != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException("Lines can only be removed from draft purchase orders.");

        var line = po.Lines.FirstOrDefault(l => l.Id == request.LineId)
            ?? throw new KeyNotFoundException($"Purchase order line {request.LineId} not found");

        if (po.Lines.Count == 1)
            throw new InvalidOperationException(
                "A purchase order needs at least one line. Add another line first, or delete the purchase order.");

        var referenced = await db.PurchaseOrderReleases.AnyAsync(r => r.PurchaseOrderLineId == line.Id, cancellationToken)
            || await db.LotRecords.AnyAsync(l => l.PurchaseOrderLineId == line.Id, cancellationToken);
        if (referenced)
            throw new InvalidOperationException("This line has releases or lots recorded against it and cannot be removed.");

        po.Lines.Remove(line);
        db.PurchaseOrderLines.Remove(line);

        var label = string.IsNullOrWhiteSpace(line.Part?.PartNumber) ? line.Description : line.Part.PartNumber;
        db.LogActivityAt("line-removed", $"Removed line {label}", ("PurchaseOrder", po.Id));

        await repo.SaveChangesAsync(cancellationToken);

        return await mediator.Send(new GetPurchaseOrderByIdQuery(po.Id), cancellationToken);
    }
}
