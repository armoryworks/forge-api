using FluentValidation;
using MediatR;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.PurchaseOrders;

// P06-4: PO lines were immutable. This adds a line-edit path, gated to Draft.
// Request.Quantity maps to PurchaseOrderLine.OrderedQuantity.
public record UpdatePurchaseOrderLineCommand(int PurchaseOrderId, int LineId, UpdateOrderLineRequestModel Data)
    : IRequest<PurchaseOrderDetailResponseModel>;

public class UpdatePurchaseOrderLineValidator : AbstractValidator<UpdatePurchaseOrderLineCommand>
{
    public UpdatePurchaseOrderLineValidator()
    {
        RuleFor(x => x.PurchaseOrderId).GreaterThan(0);
        RuleFor(x => x.LineId).GreaterThan(0);
        RuleFor(x => x.Data.Quantity).GreaterThan(0);
        RuleFor(x => x.Data.UnitPrice).GreaterThanOrEqualTo(0);
    }
}

public class UpdatePurchaseOrderLineHandler(IPurchaseOrderRepository repo, IMediator mediator)
    : IRequestHandler<UpdatePurchaseOrderLineCommand, PurchaseOrderDetailResponseModel>
{
    public async Task<PurchaseOrderDetailResponseModel> Handle(UpdatePurchaseOrderLineCommand request, CancellationToken cancellationToken)
    {
        var po = await repo.FindWithDetailsAsync(request.PurchaseOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order {request.PurchaseOrderId} not found");

        if (po.Status != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException("Only draft purchase orders can have their lines edited.");

        var line = po.Lines.FirstOrDefault(l => l.Id == request.LineId)
            ?? throw new KeyNotFoundException($"Purchase order line {request.LineId} not found");

        line.Description = request.Data.Description;
        line.OrderedQuantity = request.Data.Quantity;
        line.UnitPrice = request.Data.UnitPrice;
        line.Notes = request.Data.Notes;
        line.PurchaseOptionId = request.Data.PurchaseOptionId;

        await repo.SaveChangesAsync(cancellationToken);

        return await mediator.Send(new GetPurchaseOrderByIdQuery(request.PurchaseOrderId), cancellationToken);
    }
}
