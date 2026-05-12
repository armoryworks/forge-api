namespace Forge.Core.Models;

/// <summary>
/// Compact list-item model for the receiving-inspection-template entity
/// picker. Backed by <see cref="Forge.Core.Entities.QcChecklistTemplate"/>
/// — receiving inspections reuse the QC checklist template structure.
/// </summary>
public record ReceivingInspectionTemplateListItemModel(
    int Id,
    string Name,
    string? Description,
    bool IsActive);
