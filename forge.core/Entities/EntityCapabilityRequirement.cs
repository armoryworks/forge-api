namespace Forge.Core.Entities;

/// <summary>
/// Capability-indexed entity completeness requirement. A row says: "for an
/// entity of type X to be usable in workflows enabled by capability Y, the
/// JSON predicate Z must evaluate true against the loaded entity."
///
/// Distinct from <see cref="EntityReadinessValidator"/> (which gates workflow
/// step completion); this table answers the operational-readiness question
/// "is this entity complete enough to participate in capability Y?" — used
/// by the entity-completeness chip + future blocking checks (e.g. PO
/// creation refuses to add a vendor missing required PO fields).
///
/// Catalog ships empty in this PR; admins author rows via the
/// <c>EntityCapabilityRequirementsController</c> CRUD endpoints. Per Dan's
/// option-B choice the seeded ruleset is deferred until the install knows
/// what its capabilities actually need.
///
/// Shares <see cref="Workflows.PredicateEvaluator"/> with the workflow
/// substrate — same JSON DSL ("fieldPresent" / "fieldEquals" / "all" /
/// etc.). Composite uniqueness on (EntityType, CapabilityCode, RequirementId)
/// so multiple discrete requirements can apply to one (entity, capability)
/// pair (e.g. CAP-P2P-PO on Vendor needs both "tax-id" AND "payment-terms"
/// as two separate rows so the failure list can call them out distinctly).
/// </summary>
public class EntityCapabilityRequirement : BaseAuditableEntity
{
    /// <summary>Entity type this requirement applies to, e.g. "Vendor", "Part", "Customer".</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Capability code from the static catalog, e.g. "CAP-P2P-PO".</summary>
    public string CapabilityCode { get; set; } = string.Empty;

    /// <summary>
    /// Stable id for this specific requirement within (EntityType, CapabilityCode).
    /// Lets a capability declare multiple discrete requirements that surface
    /// individually in the chip's missing-fields list. Example values:
    /// "tax-id", "payment-terms", "billing-address".
    /// </summary>
    public string RequirementId { get; set; } = string.Empty;

    /// <summary>The DSL predicate (JSON), evaluated by PredicateEvaluator.</summary>
    public string Predicate { get; set; } = "{}";

    /// <summary>i18n key — short label shown in the chip popover ("Tax ID").</summary>
    public string DisplayNameKey { get; set; } = string.Empty;

    /// <summary>
    /// i18n key — fuller explanation in the chip popover and the future
    /// blocking-action error toast ("Vendor needs federal tax ID before
    /// being added to a purchase order").
    /// </summary>
    public string MissingMessageKey { get; set; } = string.Empty;

    /// <summary>Display-order within the (EntityType, CapabilityCode) grouping.</summary>
    public int SortOrder { get; set; }

    /// <summary>True when this row was inserted by a seeder (none today). Admin UI may lock seeded rows.</summary>
    public bool IsSeedData { get; set; }
}
