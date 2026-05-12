using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Eco;

public record AddEcoAffectedItemCommand(int EcoId, CreateEcoAffectedItemRequestModel Request) : IRequest<EcoAffectedItemResponseModel>;

public class AddEcoAffectedItemValidator : AbstractValidator<AddEcoAffectedItemCommand>
{
    public AddEcoAffectedItemValidator()
    {
        RuleFor(x => x.Request.EntityType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.EntityId).GreaterThan(0);
        RuleFor(x => x.Request.ChangeDescription).NotEmpty().MaximumLength(500);
    }
}

public class AddEcoAffectedItemHandler(AppDbContext db) : IRequestHandler<AddEcoAffectedItemCommand, EcoAffectedItemResponseModel>
{
    public async Task<EcoAffectedItemResponseModel> Handle(AddEcoAffectedItemCommand request, CancellationToken cancellationToken)
    {
        var eco = await db.EngineeringChangeOrders
            .FirstOrDefaultAsync(e => e.Id == request.EcoId, cancellationToken)
            ?? throw new KeyNotFoundException($"ECO {request.EcoId} not found");

        var item = new EcoAffectedItem
        {
            EcoId = request.EcoId,
            EntityType = request.Request.EntityType,
            EntityId = request.Request.EntityId,
            ChangeDescription = request.Request.ChangeDescription,
            OldValue = request.Request.OldValue,
            NewValue = request.Request.NewValue,
        };

        db.EcoAffectedItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return new EcoAffectedItemResponseModel
        {
            Id = item.Id,
            EntityType = item.EntityType,
            EntityId = item.EntityId,
            ChangeDescription = item.ChangeDescription,
            OldValue = item.OldValue,
            NewValue = item.NewValue,
            IsImplemented = item.IsImplemented,
        };
    }
}
