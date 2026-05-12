using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// Phase 1r / Batch 11 — admin-configured lead-assignment rules. Priority
/// determines evaluation order (low number = evaluated first). The Spec
/// JSON varies by Kind — UI renders a Kind-specific form so admins don't
/// see raw JSON.
/// </summary>
public record AssignmentRuleResponseModel(
    int Id,
    string Name,
    AssignmentRuleKind Kind,
    int Priority,
    bool IsActive,
    string? Spec,
    DateTimeOffset CreatedAt);

public record CreateAssignmentRuleRequest(
    string Name,
    AssignmentRuleKind Kind,
    int Priority,
    string? Spec);

public record UpdateAssignmentRuleRequest(
    string? Name,
    AssignmentRuleKind? Kind,
    int? Priority,
    bool IsActive,
    string? Spec);
