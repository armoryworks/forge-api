using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record UpdateUnitOfMeasureCommand(int Id, CreateUomRequestModel Data) : IRequest<UomResponseModel>;

public class UpdateUnitOfMeasureValidator : AbstractValidator<UpdateUnitOfMeasureCommand>
{
    public UpdateUnitOfMeasureValidator()
    {
        RuleFor(x => x.Data.Code).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Data.Name).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Data.Category).NotEmpty();
        RuleFor(x => x.Data.DecimalPlaces).InclusiveBetween(0, 6);
    }
}

public class UpdateUnitOfMeasureHandler(AppDbContext db)
    : IRequestHandler<UpdateUnitOfMeasureCommand, UomResponseModel>
{
    public async Task<UomResponseModel> Handle(UpdateUnitOfMeasureCommand request, CancellationToken ct)
    {
        var uom = await db.UnitsOfMeasure
            .FirstOrDefaultAsync(u => u.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"UOM {request.Id} not found.");

        var duplicate = await db.UnitsOfMeasure
            .AnyAsync(u => u.Code == request.Data.Code && u.Id != request.Id, ct);

        if (duplicate)
            throw new InvalidOperationException($"UOM code '{request.Data.Code}' already exists.");

        if (!Enum.TryParse<UomCategory>(request.Data.Category, true, out var category))
            throw new InvalidOperationException($"Invalid category '{request.Data.Category}'.");

        uom.Code = request.Data.Code.Trim().ToUpperInvariant();
        uom.Name = request.Data.Name.Trim();
        uom.Symbol = request.Data.Symbol?.Trim();
        uom.Category = category;
        uom.DecimalPlaces = request.Data.DecimalPlaces;
        uom.IsBaseUnit = request.Data.IsBaseUnit;
        uom.SortOrder = request.Data.SortOrder;

        await db.SaveChangesAsync(ct);

        return new UomResponseModel(
            uom.Id, uom.Code, uom.Name, uom.Symbol,
            uom.Category.ToString(), uom.DecimalPlaces, uom.IsBaseUnit, uom.IsActive, uom.SortOrder);
    }
}
