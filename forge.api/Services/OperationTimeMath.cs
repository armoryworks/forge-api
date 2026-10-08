using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Api.Services;

public static class OperationTimeMath
{
    public static decimal PerPieceRunMinutes(Operation op) =>
        PerPieceRunMinutes(op.RunMinutesEach, op.EstimatedMs);

    public static decimal PerPieceRunMinutes(decimal runMinutesEach, long? estimatedMs) =>
        runMinutesEach > 0m
            ? runMinutesEach
            : (estimatedMs ?? 0L) / 60000m;

    public static decimal PlannedMinutes(Operation op, decimal quantity) =>
        op.SetupMinutes + op.RunMinutesLot + PerPieceRunMinutes(op) * quantity;

    public static decimal RemainingMinutes(
        JobOperationStatus status,
        decimal setupMinutes,
        decimal lotMinutes,
        decimal perPieceMinutes,
        decimal quantity,
        decimal completedQuantity,
        decimal scrapQuantity)
    {
        if (status is JobOperationStatus.Complete or JobOperationStatus.Skipped)
            return 0m;

        var fixedMinutes = status == JobOperationStatus.NotStarted ? setupMinutes + lotMinutes : 0m;
        var piecesLeft = Math.Max(quantity - completedQuantity - scrapQuantity, 0m);
        return fixedMinutes + perPieceMinutes * piecesLeft;
    }

    public static decimal EntryMinutes(
        DateTimeOffset? timerStart, DateTimeOffset? timerStop, int durationMinutes, DateTimeOffset now)
    {
        if (timerStart is null)
            return Math.Max(durationMinutes, 0);

        var end = timerStop ?? now;
        return Math.Max((decimal)(end - timerStart.Value).TotalMinutes, 0m);
    }

    public static decimal JobBuildQuantity(Job job)
    {
        var quantity = job.JobParts
            .Where(jp => jp.PartId == job.PartId)
            .Sum(jp => jp.Quantity);
        return quantity > 0m ? quantity : 1m;
    }
}
