namespace Forge.Api.Features.Capabilities.Config;

/// <summary>
/// Phase 4 Phase-C — Body for <c>PUT /api/v1/capabilities/{id}/config</c>.
/// <see cref="ConfigJson"/> is opaque at this surface (no per-capability
/// schema validation in Phase C; Phase E/F will layer that on when
/// individual capabilities use config). <see cref="Reason"/> is captured in
/// the audit row.
/// </summary>
public record UpdateCapabilityConfigRequestModel(string ConfigJson, string? Reason = null);
