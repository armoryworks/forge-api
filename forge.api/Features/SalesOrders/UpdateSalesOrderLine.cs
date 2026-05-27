using FluentValidation;
using MediatR;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.SalesOrders;

// BE-1 / SO-8: sales-order lines were immutable. This adds a line-edit path, gated to Draft.
public record UpdateSalesOrderLineCommand(int SalesOrderId, int LineId, UpdateOrderLineRequestModel Data)
    : IRequest<SalesOrderDetailResponseModel>;

public class UpdateSalesOrderLineValidator : AbstractValidator<UpdateSalesOrderLineCommand>
{
    public UpdateSalesOrderLineValidator()
    {
        RuleFor(x => x.SalesOrderId).GreaterThan(0);
        RuleFor(x => x.LineId).GreaterThan(0);
        RuleFor(x => x.Data.Quantity).GreaterThan(0);
        RuleFor(x => x.Data.UnitPrice).GreaterThanOrEqualTo(0);
    }
}

public class UpdateSalesOrderLineHandler(ISalesOrderRepository repo, IMediator mediator)
    : IRequestHandler<UpdateSalesOrderLineCommand, SalesOrderDetailResponseModel>
{
    public async Task<SalesOrderDetailResponseModel> Handle(UpdateSalesOrderLineCommand request, CancellationToken cancellationToken)
    {
        var order = await repo.FindWithDetailsAsync(request.SalesOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {request.SalesOrderId} not found");

        if (order.Status != SalesOrderStatus.Draft)
            throw new InvalidOperationException("Only draft sales orders can have their lines edited.");

        var line = order.Lines.FirstOrDefault(l => l.Id == request.LineId)
            ?? throw new KeyNotFoundException($"Sales order line {request.LineId} not found");

        line.Description = request.Data.Description;
        line.Quantity = request.Data.Quantity;
        line.UnitPrice = request.Data.UnitPrice;
        line.Notes = request.Data.Notes;

        await repo.SaveChangesAsync(cancellationToken);

        return await mediator.Send(new GetSalesOrderByIdQuery(request.SalesOrderId), cancellationToken);
    }
}
