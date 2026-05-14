
namespace Forge.Core.Entities;

/// <summary>
/// Phase 1r / Batch 11 — admin-configured lead-assignment rules. The
/// bulk-intake handler runs through active rules in <see cref="Priority"/>
/// order; first match wins. When no rule matches, the lead falls
/// through to whichever rep pulls it from the queue.
///
/// Rule kinds:
///   • RoundRobin — rotates among a configured set of rep ids
///   • Territory — matches against the lead's state / zip / country
///   • Industry — matches against the lead's industry / SIC code
///   • AccountBased — sticky: if the company already has a related
///     lead/customer, assign to that owner
/// </summary>
public class AssignmentRule : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public AssignmentRuleKind Kind { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>JSON spec for the rule body — values vary by Kind (zip ranges, rep ids, etc.).</summary>
    public string? Spec { get; set; }
}
