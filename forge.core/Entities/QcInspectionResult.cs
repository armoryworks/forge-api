namespace Forge.Core.Entities;

public class QcInspectionResult : BaseEntity
{
    public int InspectionId { get; set; }
    public int? ChecklistItemId { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string? MeasuredValue { get; set; }
    public string? Notes { get; set; }

    public QcInspection Inspection { get; set; } = null!;
    public QcChecklistItem? ChecklistItem { get; set; }
}
