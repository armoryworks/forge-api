namespace Forge.Api.Features.Capabilities.Toggle;

/// <summary>
/// Phase 4 Phase-B / Phase-C — Body for <c>PUT /api/v1/capabilities/{id}/enabled</c>.
/// <see cref="Reason"/> is optional and captured in the audit row.
/// </summary>
public record ToggleCapabilityRequestModel(bool Enabled, string? Reason = null);
