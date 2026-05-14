using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Mrp;

public record UpdatePlannedOrderCommand(int Id, bool? IsFirmed, string? Notes) : IRequest;

public class UpdatePlannedOrderValidator : AbstractValidator<UpdatePlannedOrderCommand>
{
    public UpdatePlannedOrderValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class UpdatePlannedOrderHandler(AppDbContext db)
    : IRequestHandler<UpdatePlannedOrderCommand>
{
    public async Task Handle(UpdatePlannedOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await db.MrpPlannedOrders
            .FirstOrDefaultAsync(po => po.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Planned order {request.Id} not found.");

        if (request.IsFirmed.HasValue)
        {
            order.IsFirmed = request.IsFirmed.Value;
            order.Status = request.IsFirmed.Value ? MrpPlannedOrderStatus.Firmed : MrpPlannedOrderStatus.Planned;
        }

        if (request.Notes is not null)
            order.Notes = request.Notes;

        await db.SaveChangesAsync(cancellationToken);
    }
}
