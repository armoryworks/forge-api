using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Parts.PurchaseUnits;

// UoM purchase-units effort — edit a part's purchase unit in place.
public record UpdatePartPurchaseUnitCommand(int PartId, int Id, UpdatePartPurchaseUnitRequestModel Body)
    : IRequest<PartPurchaseUnitResponseModel>;

public class UpdatePartPurchaseUnitValidator : AbstractValidator<UpdatePartPurchaseUnitCommand>
{
    public UpdatePartPurchaseUnitValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Body.Label).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Body.ContentQuantity).GreaterThan(0m);
        RuleFor(x => x.Body.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public class UpdatePartPurchaseUnitHandler(AppDbContext db)
    : IRequestHandler<UpdatePartPurchaseUnitCommand, PartPurchaseUnitResponseModel>
{
    public async Task<PartPurchaseUnitResponseModel> Handle(UpdatePartPurchaseUnitCommand request, CancellationToken ct)
    {
        var body = request.Body;

        var option = await db.PartPurchaseUnits
            .FirstOrDefaultAsync(o => o.Id == request.Id && o.PartId == request.PartId, ct)
            ?? throw new KeyNotFoundException($"Purchase unit {request.Id} not found for part {request.PartId}");

        await PurchaseUnitUomGuard.EnsureCompatibleAsync(db, request.PartId, body.ContentUomId, ct);

        option.Label = body.Label.Trim();
        option.ContentQuantity = body.ContentQuantity;
        option.ContentUomId = body.ContentUomId;
        option.SortOrder = body.SortOrder;
        option.IsActive = body.IsActive;

        db.LogActivityAt(
            "purchase-unit-updated",
            $"Updated purchase unit: {option.Label} ({option.ContentQuantity:0.####})",
            ("Part", request.PartId));

        await db.SaveChangesAsync(ct);

        return await PartPurchaseUnitProjection.SingleAsync(db, option.Id, ct);
    }
}
