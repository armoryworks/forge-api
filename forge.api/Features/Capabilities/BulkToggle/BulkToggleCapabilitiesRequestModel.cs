namespace Forge.Api.Features.Capabilities.BulkToggle;

/// <summary>
/// Phase 4 Phase-C — Body for <c>POST /api/v1/capabilities/bulk-toggle</c>.
/// Atomic: all rows succeed or none. Used as the foundation for Phase G's
/// preset-apply (which will additionally emit a single
/// <c>PresetApplied</c> audit row).
/// </summary>
public record BulkToggleCapabilitiesRequestModel(
    IReadOnlyList<BulkToggleItem> Items,
    string? Reason = null);

public record BulkToggleItem(string Id, bool Enabled, string? IfMatch = null);
