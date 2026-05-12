namespace Forge.Core.Models;

/// <summary>
/// Workflow Pattern Phase 3 — Per-validator missing-payload entry returned
/// in the 409 envelope when a promote-status / workflow-complete request
/// is rejected because readiness gates aren't satisfied.
/// </summary>
public record MissingValidatorResponseModel(
    string ValidatorId,
    string DisplayNameKey,
    string MissingMessageKey);
