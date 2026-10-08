using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Copies one part's routing (operations plus their material links) onto another part. References
/// between operations are remapped to the copies; a reference to an operation outside the routing is
/// dropped. A material link is carried only when <c>bomMap</c> maps its source BOM line to a line on
/// the target. Nothing is saved: the copies are added to the change tracker for the caller to flush.
/// </summary>
public static class RoutingCopier
{
    public static async Task<int> CopyAsync(
        AppDbContext db,
        int sourcePartId,
        Part target,
        IReadOnlyDictionary<int, BOMLine> bomMap,
        CancellationToken ct)
    {
        var operations = await db.Operations.AsNoTracking()
            .Include(o => o.Materials)
            .Where(o => o.PartId == sourcePartId)
            .OrderBy(o => o.StepNumber).ThenBy(o => o.Id)
            .ToListAsync(ct);

        var map = new Dictionary<int, Operation>();
        foreach (var op in operations)
        {
            var copy = new Operation
            {
                Part = target,
                StepNumber = op.StepNumber,
                Title = op.Title,
                Instructions = op.Instructions,
                WorkCenterId = op.WorkCenterId,
                AssetId = op.AssetId,
                EstimatedMs = op.EstimatedMs,
                IsQcCheckpoint = op.IsQcCheckpoint,
                QcCriteria = op.QcCriteria,
                SetupMinutes = op.SetupMinutes,
                RunMinutesEach = op.RunMinutesEach,
                RunMinutesLot = op.RunMinutesLot,
                OverlapPercent = op.OverlapPercent,
                ScrapFactor = op.ScrapFactor,
                IsSubcontract = op.IsSubcontract,
                SubcontractVendorId = op.SubcontractVendorId,
                SubcontractCost = op.SubcontractCost,
                SubcontractLeadTimeDays = op.SubcontractLeadTimeDays,
                SubcontractInstructions = op.SubcontractInstructions,
                SubcontractTurnTimeDays = op.SubcontractTurnTimeDays,
                LaborRate = op.LaborRate,
                BurdenRate = op.BurdenRate,
                EstimatedLaborCost = op.EstimatedLaborCost,
                EstimatedBurdenCost = op.EstimatedBurdenCost,
            };

            foreach (var material in op.Materials)
            {
                if (!bomMap.TryGetValue(material.BomLineId, out var bomLine))
                    continue;
                copy.Materials.Add(new OperationMaterial
                {
                    Operation = copy,
                    BomLine = bomLine,
                    Quantity = material.Quantity,
                    Notes = material.Notes,
                });
            }

            db.Operations.Add(copy);
            map[op.Id] = copy;
        }

        foreach (var op in operations)
        {
            if (op.ReferencedOperationId is { } referencedId && map.TryGetValue(referencedId, out var referenced))
                map[op.Id].ReferencedOperation = referenced;
        }

        return operations.Count;
    }
}
