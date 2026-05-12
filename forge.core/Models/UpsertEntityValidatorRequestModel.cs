namespace Forge.Core.Models;

/// <summary>
/// Workflow Pattern Phase 3 — Create / update payload for entity readiness
/// validators. <see cref="ValidatorId"/> is immutable on update; the route
/// id wins.
/// </summary>
public record UpsertEntityValidatorRequestModel(
    string EntityType,
    string ValidatorId,
    string Predicate,
    string DisplayNameKey,
    string MissingMessageKey);
