namespace Forge.Core.Models;

/// <summary>
/// Phase 1r / Batch 9 — admin-managed lead-source definitions. Lead intake
/// stamps a FK to one of these rows; per-source quality scores nudge
/// every time a child lead's disposition is recorded.
/// </summary>
public record LeadSourceResponseModel(
    int Id,
    string Name,
    string Code,
    string? Description,
    int QualityScore,
    DateTimeOffset? LastScoredAt,
    bool IsActive,
    int LeadCount,
    DateTimeOffset CreatedAt);

public record CreateLeadSourceRequest(
    string Name,
    string Code,
    string? Description);

public record UpdateLeadSourceRequest(
    string? Name,
    string? Description,
    bool IsActive);
