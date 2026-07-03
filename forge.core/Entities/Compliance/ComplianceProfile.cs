using Forge.Core.Enums;

namespace Forge.Core.Entities.Compliance;

/// <summary>
/// regulated-parts-safety C-1: a per-industry compliance profile. A shop activates one or
/// more; effective requirements are the <b>additive union</b> of the active profiles
/// (general/none contributes nothing). Declares the required traceability level, the
/// required fields at named process steps (<see cref="ComplianceFieldRule"/>), and SDS
/// obligations. Enforced server-side; capabilities gate visibility, this enforces policy.
/// </summary>
public class ComplianceProfile : BaseAuditableEntity
{
    /// <summary>Stable industry slug (e.g. "firearms", "food", "medical"); unique.</summary>
    public string IndustryKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Minimum traceability this industry mandates on regulated parts.</summary>
    public TraceabilityType RequiredTraceabilityType { get; set; } = TraceabilityType.None;

    /// <summary>Whether SDS documentation is required for hazardous materials under this profile.</summary>
    public bool SdsRequired { get; set; }

    /// <summary>Admin-activated for this install. Seeded profiles start inactive.</summary>
    public bool IsActive { get; set; }

    public bool IsSystem { get; set; }

    public ICollection<ComplianceFieldRule> FieldRules { get; set; } = [];
}
