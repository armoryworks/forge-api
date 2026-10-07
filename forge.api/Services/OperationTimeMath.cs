using Forge.Core.Entities;

namespace Forge.Api.Services;

public static class OperationTimeMath
{
    public static decimal PerPieceRunMinutes(Operation op) =>
        op.RunMinutesEach > 0m
            ? op.RunMinutesEach
            : (op.EstimatedMs ?? 0L) / 60000m;

    public static decimal PlannedMinutes(Operation op, decimal quantity) =>
        op.SetupMinutes + op.RunMinutesLot + PerPieceRunMinutes(op) * quantity;

    public static decimal JobBuildQuantity(Job job)
    {
        var quantity = job.JobParts
            .Where(jp => jp.PartId == job.PartId)
            .Sum(jp => jp.Quantity);
        return quantity > 0m ? quantity : 1m;
    }
}
