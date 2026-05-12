namespace Forge.Core.Models;

public record ProjectResponseModel
{
    public int Id { get; init; }
    public string ProjectNumber { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public int? SalesOrderId { get; init; }
    public decimal BudgetTotal { get; init; }
    public decimal ActualTotal { get; init; }
    public decimal CommittedTotal { get; init; }
    public decimal EstimateAtCompletionTotal { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateOnly? PlannedStartDate { get; init; }
    public DateOnly? PlannedEndDate { get; init; }
    public DateOnly? ActualStartDate { get; init; }
    public DateOnly? ActualEndDate { get; init; }
    public decimal? PercentComplete { get; init; }
    public string? Notes { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
