using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

public record DeleteOperationMaterialCommand(int PartId, int OperationId, int MaterialId) : IRequest;

public class DeleteOperationMaterialHandler(AppDbContext db) : IRequestHandler<DeleteOperationMaterialCommand>
{
    public async Task Handle(DeleteOperationMaterialCommand request, CancellationToken cancellationToken)
    {
        var material = await db.OperationMaterials
            .Include(m => m.Operation)
            .FirstOrDefaultAsync(m => m.Id == request.MaterialId && m.OperationId == request.OperationId && m.Operation.PartId == request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Operation material {request.MaterialId} not found");

        db.OperationMaterials.Remove(material);
        await db.SaveChangesAsync(cancellationToken);
    }
}
