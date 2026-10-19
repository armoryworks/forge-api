using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
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

        var jobNumber = node.JobId is int jobId
            ? await db.Jobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.JobNumber).FirstOrDefaultAsync(cancellationToken)
            : null;

        return new SerialGenealogyResponseModel(
            node.Id,
            node.SerialValue,
            node.Part.PartNumber,
            node.Status,
            childModels,
            node.JobId,
            jobNumber,
            await LotsAsync(node, cancellationToken));
    }

    private async Task<List<SerialGenealogyLotModel>> LotsAsync(SerialNumber node, CancellationToken cancellationToken)
    {
        var lots = new List<SerialGenealogyLotModel>();
        if (node.LotRecordId is int lotId)
        {
            var own = await db.LotRecords.AsNoTracking()
                .Where(l => l.Id == lotId)
                .Select(l => new SerialGenealogyLotModel(l.Id, l.LotNumber, l.PartId, l.Part.PartNumber, "Lot", null))
                .FirstOrDefaultAsync(cancellationToken);
            if (own is not null)
                lots.Add(own);
        }

        if (node.JobId is int jobId)
        {
            var issued = await db.MaterialIssues.AsNoTracking()
                .Where(mi => mi.JobId == jobId && mi.IssueType == MaterialIssueType.Issue && mi.LotNumber != null)
                .GroupBy(mi => new { mi.PartId, mi.Part.PartNumber, LotNumber = mi.LotNumber! })
                .Select(g => new { g.Key.PartId, g.Key.PartNumber, g.Key.LotNumber, Quantity = g.Sum(mi => mi.Quantity) })
                .ToListAsync(cancellationToken);

            var lotNumbers = issued.Select(i => i.LotNumber).Distinct().ToList();
            var lotIds = await db.LotRecords.AsNoTracking()
                .Where(l => lotNumbers.Contains(l.LotNumber))
                .Select(l => new { l.Id, l.LotNumber, l.PartId })
                .ToListAsync(cancellationToken);

            lots.AddRange(issued
                .OrderBy(i => i.PartNumber).ThenBy(i => i.LotNumber)
                .Select(i => new SerialGenealogyLotModel(
                    lotIds.FirstOrDefault(l => l.LotNumber == i.LotNumber && l.PartId == i.PartId)?.Id,
                    i.LotNumber, i.PartId, i.PartNumber, "MaterialIssue", i.Quantity)));
        }

        return lots;
    }
}
