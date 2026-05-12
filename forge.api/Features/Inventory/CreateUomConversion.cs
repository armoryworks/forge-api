using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record CreateUomConversionCommand(CreateUomConversionRequestModel Data) : IRequest<UomConversionResponseModel>;

public class CreateUomConversionValidator : AbstractValidator<CreateUomConversionCommand>
{
    public CreateUomConversionValidator()
    {
        RuleFor(x => x.Data.FromUomId).GreaterThan(0);
        RuleFor(x => x.Data.ToUomId).GreaterThan(0);
        RuleFor(x => x.Data.ConversionFactor).GreaterThan(0);
    }
}

public class CreateUomConversionHandler(AppDbContext db)
    : IRequestHandler<CreateUomConversionCommand, UomConversionResponseModel>
{
    public async Task<UomConversionResponseModel> Handle(CreateUomConversionCommand request, CancellationToken ct)
    {
        var data = request.Data;

        if (data.FromUomId == data.ToUomId)
            throw new InvalidOperationException("Cannot create a conversion from a UOM to itself.");

        var exists = await db.UomConversions.AnyAsync(c =>
            c.FromUomId == data.FromUomId && c.ToUomId == data.ToUomId
            && c.PartId == data.PartId, ct);

        if (exists)
            throw new InvalidOperationException("This conversion already exists.");

        var fromUom = await db.UnitsOfMeasure.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == data.FromUomId, ct)
            ?? throw new KeyNotFoundException($"UOM {data.FromUomId} not found.");

        var toUom = await db.UnitsOfMeasure.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == data.ToUomId, ct)
            ?? throw new KeyNotFoundException($"UOM {data.ToUomId} not found.");

        var conversion = new UomConversion
        {
            FromUomId = data.FromUomId,
            ToUomId = data.ToUomId,
            ConversionFactor = data.ConversionFactor,
            PartId = data.PartId,
            IsReversible = data.IsReversible,
        };

        db.UomConversions.Add(conversion);
        await db.SaveChangesAsync(ct);

        return new UomConversionResponseModel(
            conversion.Id, conversion.FromUomId, fromUom.Code,
            conversion.ToUomId, toUom.Code,
            conversion.ConversionFactor, conversion.PartId, conversion.IsReversible);
    }
}
