using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Parts.PurchaseOptions;

// UoM purchase-options effort — edit a part's purchase option in place.
public record UpdatePartPurchaseOptionCommand(int PartId, int Id, UpdatePartPurchaseOptionRequestModel Body)
    : IRequest<PartPurchaseOptionResponseModel>;

public class UpdatePartPurchaseOptionValidator : AbstractValidator<UpdatePartPurchaseOptionCommand>
{
    public UpdatePartPurchaseOptionValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Body.Label).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Body.ContentQuantity).GreaterThan(0m);
        RuleFor(x => x.Body.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public class UpdatePartPurchaseOptionHandler(AppDbContext db)
    : IRequestHandler<UpdatePartPurchaseOptionCommand, PartPurchaseOptionResponseModel>
{
    public async Task<PartPurchaseOptionResponseModel> Handle(UpdatePartPurchaseOptionCommand request, CancellationToken ct)
    {
        var body = request.Body;

        var option = await db.PartPurchaseOptions
            .FirstOrDefaultAsync(o => o.Id == request.Id && o.PartId == request.PartId, ct)
            ?? throw new KeyNotFoundException($"Purchase option {request.Id} not found for part {request.PartId}");

        await PurchaseOptionUomGuard.EnsureCompatibleAsync(db, request.PartId, body.ContentUomId, ct);

        option.Label = body.Label.Trim();
        option.ContentQuantity = body.ContentQuantity;
        option.ContentUomId = body.ContentUomId;
        option.SortOrder = body.SortOrder;
        option.IsActive = body.IsActive;

        db.LogActivityAt(
            "purchase-option-updated",
            $"Updated purchase option: {option.Label} ({option.ContentQuantity:0.####})",
            ("Part", request.PartId));

        await db.SaveChangesAsync(ct);

        return await PartPurchaseOptionProjection.SingleAsync(db, option.Id, ct);
    }
}
