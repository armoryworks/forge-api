namespace Forge.Core.Models;

public record SixBigLossesModel
{
    public int WorkCenterId { get; init; }
    public decimal EquipmentFailureMinutes { get; init; }
    public decimal SetupAdjustmentMinutes { get; init; }
    public decimal IdlingMinutes { get; init; }
    public decimal ReducedSpeedMinutes { get; init; }
    public decimal ProcessDefectMinutes { get; init; }
    public decimal ReducedYieldMinutes { get; init; }
    public decimal TotalLossMinutes { get; init; }
}
