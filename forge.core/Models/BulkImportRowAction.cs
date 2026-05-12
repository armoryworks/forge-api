namespace Forge.Core.Models;

/// <summary>
/// Per-row outcome for the price-list-entry CSV bulk import flow. Returned in
/// both the dry-run preview (<see cref="BulkImportRowPreview"/>) and the apply
/// result (<see cref="BulkImportRowResult"/>) so the UI can color-code each
/// row consistently.
/// </summary>
public enum BulkImportRowAction
{
    Add,
    Update,
    Skip,
    Error,
}
