namespace Forge.Api.Capabilities.Discovery.Bundles;

/// <summary>
/// Pro Services rollout (Artifact 5 §3.4) — per-preset role bundle.
/// Apply-preset upserts the contained role seeds into <c>role_templates</c>
/// (and into ASP.NET Identity roles via the seeder), honoring the
/// conflict policy.
///
/// PRESET-08 seeds Pro Services roles (Practitioner, Engagement Manager,
/// Account Manager, Delivery Lead). PRESET-09 (Hybrid) seeds the union.
/// PRESET-04 carries the existing manufacturing roles (refactored out
/// of <c>SeedData.Essential</c> in a later task).
///
/// Default conflict policy is <see cref="RoleConflictPolicy.AddOnly"/> —
/// roles tend to accumulate org-specific permission grants over time;
/// re-applying a preset must never strip permissions an admin added.
/// </summary>
public sealed record RoleBundle(
    IReadOnlyList<RoleSeed> Roles,
    RoleConflictPolicy ConflictPolicy = RoleConflictPolicy.AddOnly);

/// <summary>One role to seed.</summary>
/// <param name="Code">Stable code, e.g. <c>"engagement_manager"</c>.</param>
/// <param name="Name">Human display name.</param>
/// <param name="Description">Optional role description shown in admin.</param>
/// <param name="DefaultCapabilities">Capability codes (CAP-*) granted by default to members of this role.</param>
/// <param name="DefaultPermissions">Permission keys granted by default to members of this role.</param>
public sealed record RoleSeed(
    string Code,
    string Name,
    string? Description = null,
    IReadOnlyList<string>? DefaultCapabilities = null,
    IReadOnlyList<string>? DefaultPermissions = null);

/// <summary>How apply-preset handles roles that already exist.</summary>
public enum RoleConflictPolicy
{
    /// <summary>Add missing roles; never modify or remove existing roles (default — safest).</summary>
    AddOnly,
    /// <summary>Add or update by code (replaces description / default-permission grants).</summary>
    UpsertByCode
}
