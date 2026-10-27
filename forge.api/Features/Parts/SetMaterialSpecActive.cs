using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Parts;

public record SetMaterialSpecActiveCommand(int Id, bool IsActive) : IRequest<MaterialSpecResponseModel>;

/// <summary>
/// Retires a material spec or category so it no longer appears in the part pickers, or brings it back. Parts that
/// already use it keep it. A category with specs still in use cannot be retired until its specs are; a spec cannot
/// be reactivated under a retired category.
/// </summary>
public class SetMaterialSpecActiveHandler(AppDbContext db)
    : IRequestHandler<SetMaterialSpecActiveCommand, MaterialSpecResponseModel>
{
    public async Task<MaterialSpecResponseModel> Handle(SetMaterialSpecActiveCommand request, CancellationToken cancellationToken)
    {
        var row = await db.ReferenceData.FirstOrDefaultAsync(
                r => r.Id == request.Id && r.GroupCode == CreateMaterialSpecHandler.GroupCode, cancellationToken)
            ?? throw new KeyNotFoundException($"Material spec {request.Id} not found");

        if (row.IsActive != request.IsActive)
        {
            if (!request.IsActive && row.ParentId is null && await db.ReferenceData.AnyAsync(
                    r => r.GroupCode == CreateMaterialSpecHandler.GroupCode && r.ParentId == row.Id && r.IsActive, cancellationToken))
                throw new InvalidOperationException($"Retire the specs under {row.Label} first.");

            if (request.IsActive && row.ParentId is int parentId && await db.ReferenceData.AnyAsync(
                    r => r.Id == parentId && !r.IsActive, cancellationToken))
                throw new InvalidOperationException("Reactivate its category first.");

            row.IsActive = request.IsActive;
            db.LogActivityAt(request.IsActive ? "reactivated" : "retired",
                $"Material {(request.IsActive ? "reactivated" : "retired")}: {row.Label}", ("ReferenceData", row.Id));
            await db.SaveChangesAsync(cancellationToken);
        }

        return await MaterialSpecLookup.OneAsync(db, row.Id, cancellationToken);
    }
}
