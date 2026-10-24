using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// Phase 1r / Batch 11 — admin-configured lead-assignment rules, evaluated in
/// <see cref="Priority"/> order (then name) whenever a lead is created without
/// an owner — manual entry, web-form intake and bulk import. The first active
/// rule that matches and names an active rep wins.
///
/// Rule kinds (every kind except AccountBased hands the lead to the spec's
/// <c>repIds</c> in rotation):
///   • RoundRobin — matches every lead
///   • Territory — matches the lead's account state (<c>states</c>) or
///     postal-code prefix (<c>zipPrefixes</c>)
///   • Industry — matches the lead's account industry (<c>industries</c>)
///   • AccountBased — sticky: the lead's account owner, else the owner of
///     an earlier lead for the same company
/// </summary>
public class AssignmentRule : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public AssignmentRuleKind Kind { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>JSON spec for the rule body — values vary by Kind (zip ranges, rep ids, etc.).</summary>
    public string? Spec { get; set; }
    /// <summary>
    /// Round-robin cursor: the rep this rule assigned most recently. The next lead goes to the
    /// rep after this one in the spec's <c>repIds</c> list (the first rep when null or no longer listed).
    /// </summary>
    public int? LastAssignedUserId { get; set; }
}
