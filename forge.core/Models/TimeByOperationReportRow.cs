namespace Forge.Core.Models;

public record TimeByOperationReportRow
{
    public int PartId { get; init; }
    public string PartNumber { get; init; } = string.Empty;
    public int OperationId { get; init; }
    public string OperationName { get; init; } = string.Empty;
    public int JobCount { get; init; }
    public decimal AvgSetupMinutes { get; init; }
    public decimal AvgRunMinutesPerPiece { get; init; }
    public decimal TotalHours { get; init; }
    public decimal EstimatedHours { get; init; }
    public decimal VariancePercent { get; init; }
}
