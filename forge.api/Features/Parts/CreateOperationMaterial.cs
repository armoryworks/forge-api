using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

public record CreateOperationMaterialCommand(int PartId, int OperationId, CreateOperationMaterialRequestModel Data) : IRequest<OperationMaterialResponseModel>;

public class CreateOperationMaterialValidator : AbstractValidator<CreateOperationMaterialCommand>
{
    public CreateOperationMaterialValidator()
    {
        RuleFor(x => x.PartId).GreaterThan(0);
        RuleFor(x => x.OperationId).GreaterThan(0);
        RuleFor(x => x.Data.BomEntryId).GreaterThan(0);
        RuleFor(x => x.Data.Quantity).GreaterThan(0);
        RuleFor(x => x.Data.Notes).MaximumLength(1000).When(x => x.Data.Notes is not null);
    }
}

public class CreateOperationMaterialHandler(AppDbContext db) : IRequestHandler<CreateOperationMaterialCommand, OperationMaterialResponseModel>
{
    public async Task<OperationMaterialResponseModel> Handle(CreateOperationMaterialCommand request, CancellationToken cancellationToken)
    {
        var operation = await db.Operations.FirstOrDefaultAsync(o => o.Id == request.OperationId && o.PartId == request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {request.OperationId} not found for part {request.PartId}");

        var bomEntry = await db.BOMEntries.Include(b => b.ChildPart).FirstOrDefaultAsync(b => b.Id == request.Data.BomEntryId && b.ParentPartId == request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"BOM entry {request.Data.BomEntryId} not found for part {request.PartId}");

        var material = new OperationMaterial
        {
            OperationId = request.OperationId,
            BomEntryId = request.Data.BomEntryId,
            Quantity = request.Data.Quantity,
            Notes = request.Data.Notes?.Trim(),
        };

        db.OperationMaterials.Add(material);
        await db.SaveChangesAsync(cancellationToken);

        return new OperationMaterialResponseModel(
            material.Id,
            material.OperationId,
            material.BomEntryId,
            bomEntry.ChildPart.PartNumber,
            bomEntry.ChildPart.Name,
            material.Quantity,
            material.Notes);
    }
}
