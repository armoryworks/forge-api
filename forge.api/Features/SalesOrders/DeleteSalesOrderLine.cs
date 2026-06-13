using FluentValidation;
using MediatR;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders;

// SALES-LINE-CRUD: remove a line from a draft sales order. SalesOrderLine is a
// BaseEntity (hard delete via cascade). Draft-only, so no shipments/jobs hang
// off the line yet. The order must keep at least one line.
public record DeleteSalesOrderLineCommand(int SalesOrderId, int LineId)
    : IRequest<SalesOrderDetailResponseModel>;

public class DeleteSalesOrderLineValidator : AbstractValidator<DeleteSalesOrderLineCommand>
{
    public DeleteSalesOrderLineValidator()
    {
        RuleFor(x => x.SalesOrderId).GreaterThan(0);
        RuleFor(x => x.LineId).GreaterThan(0);
    }
}

public class DeleteSalesOrderLineHandler(ISalesOrderRepository repo, AppDbContext db, IMediator mediator)
    : IRequestHandler<DeleteSalesOrderLineCommand, SalesOrderDetailResponseModel>
{
    public async Task<SalesOrderDetailResponseModel> Handle(DeleteSalesOrderLineCommand request, CancellationToken cancellationToken)
    {
        var order = await repo.FindWithDetailsAsync(request.SalesOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {request.SalesOrderId} not found");

        if (order.Status != SalesOrderStatus.Draft)
            throw new InvalidOperationException("Only draft sales orders can have lines removed.");

        var line = order.Lines.FirstOrDefault(l => l.Id == request.LineId)
            ?? throw new KeyNotFoundException($"Sales order line {request.LineId} not found");

        if (order.Lines.Count == 1)
            throw new InvalidOperationException("A sales order must have at least one line item.");

        order.Lines.Remove(line);

        db.LogActivityAt(
            "line-removed",
            $"Removed order line {line.LineNumber}: {line.Description}",
            ("SalesOrder", order.Id));

        await repo.SaveChangesAsync(cancellationToken);

        return await mediator.Send(new GetSalesOrderByIdQuery(request.SalesOrderId), cancellationToken);
    }
}
