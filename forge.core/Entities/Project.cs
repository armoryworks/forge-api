
namespace Forge.Core.Entities;

public class Project : BaseAuditableEntity
{
    public string ProjectNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? CustomerId { get; set; }
    public int? SalesOrderId { get; set; }
    public decimal BudgetTotal { get; set; }
    public decimal ActualTotal { get; set; }
    public decimal CommittedTotal { get; set; }
    public decimal EstimateAtCompletionTotal { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Planning;
    public DateOnly? PlannedStartDate { get; set; }
    public DateOnly? PlannedEndDate { get; set; }
    public DateOnly? ActualStartDate { get; set; }
    public DateOnly? ActualEndDate { get; set; }
    public decimal? RevenueRecognized { get; set; }
    public decimal? PercentComplete { get; set; }
    public string? Notes { get; set; }

    public Customer? Customer { get; set; }
    public SalesOrder? SalesOrder { get; set; }
    public ICollection<WbsElement> WbsElements { get; set; } = [];
}
