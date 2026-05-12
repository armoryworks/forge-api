namespace Forge.Core.Models;

/// <summary>
/// Workflow Pattern Phase 3 — Entity readiness validator response.
/// </summary>
public record EntityValidatorResponseModel(
    int Id,
    string EntityType,
    string ValidatorId,
    string Predicate,
    string DisplayNameKey,
    string MissingMessageKey,
    bool IsSeedData);
