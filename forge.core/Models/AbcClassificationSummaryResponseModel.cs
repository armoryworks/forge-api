namespace Forge.Core.Models;

public record AbcClassificationSummaryResponseModel
{
    public DateTimeOffset? LastRunDate { get; init; }
    public int ClassACount { get; init; }
    public int ClassBCount { get; init; }
    public int ClassCCount { get; init; }
    public decimal ClassAValuePercent { get; init; }
    public decimal ClassBValuePercent { get; init; }
    public decimal ClassCValuePercent { get; init; }
}
