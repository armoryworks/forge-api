namespace Forge.Core.Models;

public record SubcontractSpendingRow
{
    public int VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public string OperationType { get; init; } = string.Empty;
    public int OrderCount { get; init; }
    public decimal TotalSpend { get; init; }
    public decimal AvgLeadTimeDays { get; init; }
    public decimal OnTimePercent { get; init; }
    public decimal QualityAcceptPercent { get; init; }
}
