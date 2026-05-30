using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Parts.PurchaseOptions;

// UoM purchase-options effort — add a purchasable size/form to a part.
public record CreatePartPurchaseOptionCommand(int PartId, CreatePartPurchaseOptionRequestModel Body)
    : IRequest<PartPurchaseOptionResponseModel>;

public class CreatePartPurchaseOptionValidator : AbstractValidator<CreatePartPurchaseOptionCommand>
{
    public CreatePartPurchaseOptionValidator()
    {
        RuleFor(x => x.PartId).GreaterThan(0);
        RuleFor(x => x.Body.Label).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Body.ContentQuantity).GreaterThan(0m);
        RuleFor(x => x.Body.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public class CreatePartPurchaseOptionHandler(AppDbContext db)
    : IRequestHandler<CreatePartPurchaseOptionCommand, PartPurchaseOptionResponseModel>
{
    public async Task<PartPurchaseOptionResponseModel> Handle(CreatePartPurchaseOptionCommand request, CancellationToken ct)
    {
        var body = request.Body;

        if (!await db.Parts.AnyAsync(p => p.Id == request.PartId, ct))
            throw new KeyNotFoundException($"Part {request.PartId} not found");

        await PurchaseOptionUomGuard.EnsureCompatibleAsync(db, request.PartId, body.ContentUomId, ct);

        var option = new PartPurchaseOption
        {
            PartId = request.PartId,
            Label = body.Label.Trim(),
            ContentQuantity = body.ContentQuantity,
            ContentUomId = body.ContentUomId,
            SortOrder = body.SortOrder,
            IsActive = true,
        };

        db.PartPurchaseOptions.Add(option);

        db.LogActivityAt(
            "purchase-option-added",
            $"Added purchase option: {option.Label} ({option.ContentQuantity:0.####})",
            ("Part", request.PartId));

        await db.SaveChangesAsync(ct);

        return await PartPurchaseOptionProjection.SingleAsync(db, option.Id, ct);
    }
}
