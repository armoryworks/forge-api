using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class ReviewCycle : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public ReviewCycleStatus Status { get; set; } = ReviewCycleStatus.Draft;
    public string? Description { get; set; }

    public ICollection<PerformanceReview> Reviews { get; set; } = [];
}
