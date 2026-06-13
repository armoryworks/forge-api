using FluentValidation;
using MediatR;

using Forge.Api.Validation;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders;

// SALES-LINE-CRUD: append a line to a draft sales order, mirroring
// UpdateSalesOrderLine. Gated to Draft — once confirmed, lines back shipments
// and jobs, so they must not change here.
public record AddSalesOrderLineCommand(int SalesOrderId, CreateSalesOrderLineModel Data)
    : IRequest<SalesOrderDetailResponseModel>;

public class AddSalesOrderLineValidator : AbstractValidator<AddSalesOrderLineCommand>
{
    public AddSalesOrderLineValidator()
    {
        RuleFor(x => x.SalesOrderId).GreaterThan(0);
        RuleFor(x => x.Data.Description).NotEmpty();
        RuleFor(x => x.Data.Quantity).GreaterThan(0);
        RuleFor(x => x.Data.UnitPrice).GreaterThanOrEqualTo(0);
    }
}

public class AddSalesOrderLineHandler(
    ISalesOrderRepository repo,
    IPartRepository partRepo,
    AppDbContext db,
    IMediator mediator)
    : IRequestHandler<AddSalesOrderLineCommand, SalesOrderDetailResponseModel>
{
    public async Task<SalesOrderDetailResponseModel> Handle(AddSalesOrderLineCommand request, CancellationToken cancellationToken)
    {
        var order = await repo.FindWithDetailsAsync(request.SalesOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {request.SalesOrderId} not found");

        if (order.Status != SalesOrderStatus.Draft)
            throw new InvalidOperationException("Only draft sales orders can have lines added.");

        if (request.Data.PartId is int partId && partId > 0)
        {
            var part = await partRepo.FindAsync(partId, cancellationToken);
            ActiveCheck.EnsureActive(part, "Part", "partId", partId);
        }

        var nextLineNumber = order.Lines.Count == 0 ? 1 : order.Lines.Max(l => l.LineNumber) + 1;

        order.Lines.Add(new SalesOrderLine
        {
            PartId = request.Data.PartId,
            Description = request.Data.Description,
            Quantity = request.Data.Quantity,
            UnitPrice = request.Data.UnitPrice,
            LineNumber = nextLineNumber,
            Notes = request.Data.Notes,
        });

        db.LogActivityAt(
            "line-added",
            $"Added order line {nextLineNumber}: {request.Data.Description} ({request.Data.Quantity} × {request.Data.UnitPrice})",
            ("SalesOrder", order.Id));

        await repo.SaveChangesAsync(cancellationToken);

        return await mediator.Send(new GetSalesOrderByIdQuery(request.SalesOrderId), cancellationToken);
    }
}
