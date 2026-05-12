using Forge.Core.Enums;

namespace Forge.Core.Models;

public record UpdateProjectRequestModel
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public int? CustomerId { get; init; }
    public decimal? BudgetTotal { get; init; }
    public ProjectStatus? Status { get; init; }
    public DateOnly? PlannedStartDate { get; init; }
    public DateOnly? PlannedEndDate { get; init; }
    public DateOnly? ActualStartDate { get; init; }
    public DateOnly? ActualEndDate { get; init; }
    public decimal? PercentComplete { get; init; }
    public string? Notes { get; init; }
}
