namespace Forge.Core.Entities;

public class AbcClassificationRun : BaseEntity
{
    public DateTimeOffset RunDate { get; set; }
    public int TotalParts { get; set; }
    public int ClassACount { get; set; }
    public int ClassBCount { get; set; }
    public int ClassCCount { get; set; }
    public decimal ClassAThresholdPercent { get; set; }
    public decimal ClassBThresholdPercent { get; set; }
    public decimal TotalAnnualUsageValue { get; set; }
    public int LookbackMonths { get; set; } = 12;

    public ICollection<AbcClassification> Classifications { get; set; } = [];
}
