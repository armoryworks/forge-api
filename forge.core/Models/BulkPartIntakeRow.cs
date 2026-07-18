namespace Forge.Core.Models;

/// <summary>One inbound row for part bulk-intake. ExternalRowKey lets the UI match preview
/// results back to its source rows. ProcurementSource / InventoryClass arrive as free text
/// (the CSV author's words) and are parsed leniently server-side, falling back to Buy /
/// Component. ExternalId carries the author's own/legacy part number — Forge issues its own
/// sequential part number on commit, so the supplied identifier is preserved as a reference
/// and used as a secondary dedup key.</summary>
public record BulkPartIntakeRow(
    string? ExternalRowKey,
    string Name,
    string? Description,
    string? ProcurementSource,
    string? InventoryClass,
    string? ExternalId);
