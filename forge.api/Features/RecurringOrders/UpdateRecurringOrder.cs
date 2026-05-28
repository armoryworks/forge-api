using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.RecurringOrders;

// BE-4 — in-place edit of a recurring order (header + optional line replacement).
public record UpdateRecurringOrderCommand(int Id, UpdateRecurringOrderRequestModel Data)
    : IRequest<RecurringOrderDetailResponseModel>;

public class UpdateRecurringOrderValidator : AbstractValidator<UpdateRecurringOrderCommand>
{
    public UpdateRecurringOrderValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.Name).NotEmpty().MaximumLength(100).When(x => x.Data.Name is not null);
        RuleFor(x => x.Data.IntervalDays).GreaterThan(0).When(x => x.Data.IntervalDays.HasValue);
        RuleFor(x => x.Data.Notes).MaximumLength(2000).When(x => x.Data.Notes is not null);
        // Lines are tri-state: null = leave unchanged. A provided list must be non-empty
        // (a recurring order with no lines can't generate an order) and each line valid.
        RuleFor(x => x.Data.Lines).NotEmpty()
            .When(x => x.Data.Lines is not null)
            .WithMessage("At least one line item is required");
        RuleForEach(x => x.Data.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.PartId).GreaterThan(0);
            line.RuleFor(l => l.Description).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0m);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0);
        }).When(x => x.Data.Lines is not null);
    }
}

public class UpdateRecurringOrderHandler(IRecurringOrderRepository repo, IMediator mediator)
    : IRequestHandler<UpdateRecurringOrderCommand, RecurringOrderDetailResponseModel>
{
    public async Task<RecurringOrderDetailResponseModel> Handle(UpdateRecurringOrderCommand request, CancellationToken cancellationToken)
    {
        var ro = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Recurring order {request.Id} not found");

        var data = request.Data;

        if (data.Name is not null) ro.Name = data.Name.Trim();
        if (data.ShippingAddressId.HasValue) ro.ShippingAddressId = data.ShippingAddressId.Value;
        if (data.IntervalDays.HasValue) ro.IntervalDays = data.IntervalDays.Value;
        if (data.NextGenerationDate.HasValue) ro.NextGenerationDate = data.NextGenerationDate.Value;
        if (data.Notes is not null) ro.Notes = data.Notes;
        if (data.IsActive.HasValue) ro.IsActive = data.IsActive.Value;

        // Full line replacement when provided. Cascade-delete on the line FK removes the
        // orphaned rows; renumber from 1 so the line sequence stays contiguous.
        if (data.Lines is not null)
        {
            ro.Lines.Clear();
            var lineNumber = 1;
            foreach (var line in data.Lines)
            {
                ro.Lines.Add(new RecurringOrderLine
                {
                    PartId = line.PartId,
                    Description = line.Description,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    LineNumber = lineNumber++,
                });
            }
        }

        await repo.SaveChangesAsync(cancellationToken);

        // Re-project through the canonical detail query so the shape matches GET.
        return await mediator.Send(new GetRecurringOrderByIdQuery(ro.Id), cancellationToken);
    }
}
