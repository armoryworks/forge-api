using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Parts;

public record UpdateMaterialSpecCommand(int Id, UpdateMaterialSpecRequestModel Body) : IRequest<MaterialSpecResponseModel>;

public class UpdateMaterialSpecValidator : AbstractValidator<UpdateMaterialSpecCommand>
{
    public UpdateMaterialSpecValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Body.Label).NotEmpty().Must(l => !string.IsNullOrWhiteSpace(l)).MaximumLength(200);
    }
}

/// <summary>
/// Renames a material spec or category. The code stays as it was, so parts and imports that reference it keep
/// working; a name already used by a sibling is refused.
/// </summary>
public class UpdateMaterialSpecHandler(AppDbContext db)
    : IRequestHandler<UpdateMaterialSpecCommand, MaterialSpecResponseModel>
{
    public async Task<MaterialSpecResponseModel> Handle(UpdateMaterialSpecCommand request, CancellationToken cancellationToken)
    {
        var row = await db.ReferenceData.FirstOrDefaultAsync(
                r => r.Id == request.Id && r.GroupCode == CreateMaterialSpecHandler.GroupCode, cancellationToken)
            ?? throw new KeyNotFoundException($"Material spec {request.Id} not found");

        var label = request.Body.Label.Trim();
        if (label != row.Label)
        {
            var lowered = label.ToLowerInvariant();
            var taken = await db.ReferenceData.AnyAsync(r => r.GroupCode == CreateMaterialSpecHandler.GroupCode
                && r.ParentId == row.ParentId && r.Id != row.Id && r.Label.ToLower() == lowered, cancellationToken);
            if (taken)
                throw new InvalidOperationException($"That name is already used: {label}.");

            var previous = row.Label;
            row.Label = label;
            db.LogActivityAt("updated", $"Material renamed: {previous} to {label}", ("ReferenceData", row.Id));
            await db.SaveChangesAsync(cancellationToken);
        }

        return await MaterialSpecLookup.OneAsync(db, row.Id, cancellationToken);
    }
}
