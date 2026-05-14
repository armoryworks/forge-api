using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record CreateUnitOfMeasureCommand(CreateUomRequestModel Data) : IRequest<UomResponseModel>;

public class CreateUnitOfMeasureValidator : AbstractValidator<CreateUnitOfMeasureCommand>
{
    public CreateUnitOfMeasureValidator()
    {
        RuleFor(x => x.Data.Code).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Data.Name).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Data.Category).NotEmpty();
        RuleFor(x => x.Data.DecimalPlaces).InclusiveBetween(0, 6);
    }
}

public class CreateUnitOfMeasureHandler(AppDbContext db)
    : IRequestHandler<CreateUnitOfMeasureCommand, UomResponseModel>
{
    public async Task<UomResponseModel> Handle(CreateUnitOfMeasureCommand request, CancellationToken ct)
    {
        var exists = await db.UnitsOfMeasure
            .AnyAsync(u => u.Code == request.Data.Code, ct);

        if (exists)
            throw new InvalidOperationException($"UOM code '{request.Data.Code}' already exists.");

        if (!Enum.TryParse<UomCategory>(request.Data.Category, true, out var category))
            throw new InvalidOperationException($"Invalid category '{request.Data.Category}'.");

        var uom = new UnitOfMeasure
        {
            Code = request.Data.Code.Trim().ToUpperInvariant(),
            Name = request.Data.Name.Trim(),
            Symbol = request.Data.Symbol?.Trim(),
            Category = category,
            DecimalPlaces = request.Data.DecimalPlaces,
            IsBaseUnit = request.Data.IsBaseUnit,
            SortOrder = request.Data.SortOrder,
        };

        db.UnitsOfMeasure.Add(uom);
        await db.SaveChangesAsync(ct);

        return new UomResponseModel(
            uom.Id, uom.Code, uom.Name, uom.Symbol,
            uom.Category.ToString(), uom.DecimalPlaces, uom.IsBaseUnit, uom.IsActive, uom.SortOrder);
    }
}
