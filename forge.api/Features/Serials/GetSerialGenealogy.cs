using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Serials;

public record GetSerialGenealogyQuery(string SerialValue) : IRequest<SerialGenealogyResponseModel>;

public class GetSerialGenealogyHandler(AppDbContext db) : IRequestHandler<GetSerialGenealogyQuery, SerialGenealogyResponseModel>
{
    public async Task<SerialGenealogyResponseModel> Handle(GetSerialGenealogyQuery request, CancellationToken cancellationToken)
    {
        var serial = await db.SerialNumbers.AsNoTracking()
            .Include(s => s.Part)
            .FirstOrDefaultAsync(s => s.SerialValue == request.SerialValue, cancellationToken)
            ?? throw new KeyNotFoundException($"Serial number '{request.SerialValue}' not found");

        // Walk up to root
        var root = serial;
        while (root.ParentSerialId.HasValue)
        {
            var parent = await db.SerialNumbers.AsNoTracking()
                .Include(s => s.Part)
                .FirstOrDefaultAsync(s => s.Id == root.ParentSerialId, cancellationToken);
            if (parent == null) break;
            root = parent;
        }

        return await BuildTreeAsync(root, cancellationToken);
    }

    private async Task<SerialGenealogyResponseModel> BuildTreeAsync(SerialNumber node, CancellationToken cancellationToken)
    {
        var children = await db.SerialNumbers.AsNoTracking()
            .Include(s => s.Part)
            .Where(s => s.ParentSerialId == node.Id)
            .ToListAsync(cancellationToken);

        var childModels = new List<SerialGenealogyResponseModel>();
        foreach (var child in children)
        {
            childModels.Add(await BuildTreeAsync(child, cancellationToken));
        }

        return new SerialGenealogyResponseModel(
            node.Id,
            node.SerialValue,
            node.Part.PartNumber,
            node.Status,
            childModels);
    }
}
