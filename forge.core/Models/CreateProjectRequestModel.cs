namespace Forge.Core.Models;

public record CreateProjectRequestModel
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int? CustomerId { get; init; }
    public int? SalesOrderId { get; init; }
    public decimal BudgetTotal { get; init; }
    public DateOnly? PlannedStartDate { get; init; }
    public DateOnly? PlannedEndDate { get; init; }
    public string? Notes { get; init; }
}
