namespace Forge.Core.Entities;

/// <summary>
/// Workflow Pattern Phase 2 / D6 — Entity readiness validators stored as
/// data so workflows can be re-authored without code releases. The shared
/// PredicateEvaluator interprets the JSON DSL <see cref="Predicate"/> on
/// each tier. Composite primary key is (EntityType, ValidatorId).
///
/// <see cref="DisplayNameKey"/> is the canonical noun (used in admin
/// listings and "Draft — missing BOM" rendering); <see cref="MissingMessageKey"/>
/// is the failure phrasing surfaced by the entity status promotion endpoint.
/// </summary>
public class EntityReadinessValidator : BaseAuditableEntity
{
    /// <summary>Entity type this validator applies to, e.g. "Part".</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Stable id within the entity type, e.g. "hasBom".</summary>
    public string ValidatorId { get; set; } = string.Empty;

    /// <summary>The DSL predicate (JSON), evaluated by PredicateEvaluator.</summary>
    public string Predicate { get; set; } = "{}";

    /// <summary>
    /// Optional applicability check (JSON DSL, same evaluator). When non-null
    /// and the predicate against the entity returns FALSE, this validator is
    /// skipped entirely — neither evaluated nor reported as missing. Enables
    /// per-record / per-profile readiness: a validator applies only to the
    /// records that actually need it.
    ///
    /// Examples:
    ///   • <c>{"type":"fieldEquals","field":"requiresEngineeringAttention","value":true}</c>
    ///     — only fires for records that opted into engineering review.
    ///   • <c>{"type":"fieldEquals","field":"internationalShipping","value":true}</c>
    ///     — HTS-code validator that only matters for international shipments.
    ///
    /// NULL (default) = always-applicable, matching pre-applicability behavior.
    /// </summary>
    public string? ApplicabilityPredicate { get; set; }

    /// <summary>i18n key — canonical noun (e.g. "validators.parts.hasBom").</summary>
    public string DisplayNameKey { get; set; } = string.Empty;

    /// <summary>i18n key — failure phrasing (e.g. "validators.parts.hasBomMissing").</summary>
    public string MissingMessageKey { get; set; } = string.Empty;

    /// <summary>True when this row was inserted by the seeder. Admin UI may lock seeds.</summary>
    public bool IsSeedData { get; set; }
}
