namespace Forge.Api.Capabilities;

/// <summary>
/// Phase 4 Phase-A — In-memory representation of a single catalog row.
/// The static catalog (<see cref="CapabilityCatalog"/>) holds the authoritative
/// list of every capability the application knows about. The seeder upserts
/// these into the <c>capabilities</c> table on startup.
/// </summary>
/// <param name="Code">Stable capability ID, e.g. "CAP-MD-CUSTOMERS".</param>
/// <param name="Area">Functional area code (IDEN, MD, P2P, ...).</param>
/// <param name="Name">Human display name.</param>
/// <param name="Description">Catalog description.</param>
/// <param name="IsDefaultOn">True = default-on for fresh installs.</param>
/// <param name="RequiresRoles">Optional CSV list of roles allowed to manage this capability. Null = no restriction.</param>
public sealed record CapabilityDefinition(
    string Code,
    string Area,
    string Name,
    string Description,
    bool IsDefaultOn,
    string? RequiresRoles = null);

/// <summary>
/// Phase 4 Phase-C — Encodes the dependency / soft-mutex graph from the
/// Phase 4A capability catalog. Edges are evaluated at toggle time by
/// <see cref="CapabilityDependencyResolver"/> to enforce 4D §8.2 / §8.3 /
/// 4D-decisions-log #7 (block-with-informative-error semantics).
/// </summary>
/// <param name="From">The capability whose toggle is being evaluated.</param>
/// <param name="To">The other capability participating in the relationship.</param>
public sealed record CapabilityEdge(string From, string To);
