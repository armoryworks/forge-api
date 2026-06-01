using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Parts.PurchaseUnits;

// UoM purchase-units effort — add a purchasable size/form to a part.
public record CreatePartPurchaseUnitCommand(int PartId, CreatePartPurchaseUnitRequestModel Body)
    : IRequest<PartPurchaseUnitResponseModel>;

public class CreatePartPurchaseUnitValidator : AbstractValidator<CreatePartPurchaseUnitCommand>
{
    public CreatePartPurchaseUnitValidator()
    {
        RuleFor(x => x.PartId).GreaterThan(0);
        RuleFor(x => x.Body.Label).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Body.ContentQuantity).GreaterThan(0m);
        RuleFor(x => x.Body.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public class CreatePartPurchaseUnitHandler(AppDbContext db)
    : IRequestHandler<CreatePartPurchaseUnitCommand, PartPurchaseUnitResponseModel>
{
    public async Task<PartPurchaseUnitResponseModel> Handle(CreatePartPurchaseUnitCommand request, CancellationToken ct)
    {
        var body = request.Body;

        if (!await db.Parts.AnyAsync(p => p.Id == request.PartId, ct))
            throw new KeyNotFoundException($"Part {request.PartId} not found");

        await PurchaseUnitUomGuard.EnsureCompatibleAsync(db, request.PartId, body.ContentUomId, ct);

        var option = new PartPurchaseUnit
        {
            PartId = request.PartId,
            Label = body.Label.Trim(),
            ContentQuantity = body.ContentQuantity,
            ContentUomId = body.ContentUomId,
            SortOrder = body.SortOrder,
            IsActive = true,
        };

        db.PartPurchaseUnits.Add(option);

        db.LogActivityAt(
            "purchase-unit-added",
            $"Added purchase unit: {option.Label} ({option.ContentQuantity:0.####})",
            ("Part", request.PartId));

        await db.SaveChangesAsync(ct);

        return await PartPurchaseUnitProjection.SingleAsync(db, option.Id, ct);
    }
}
