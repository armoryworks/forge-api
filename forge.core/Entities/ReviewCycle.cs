using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class ReviewCycle : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public ReviewCycleStatus Status { get; set; } = ReviewCycleStatus.Draft;
    public string? Description { get; set; }
    public int RatingScaleMax { get; set; } = 5;
    public string? CompetenciesJson { get; set; }
    public string? ScopeType { get; set; }
    public string? ScopeDepartment { get; set; }
    public string? ScopeUserIdsJson { get; set; }
    public DateTimeOffset? LaunchedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    public ICollection<PerformanceReview> Reviews { get; set; } = [];
}
