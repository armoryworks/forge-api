namespace Forge.Core.Models;

/// <summary>
/// Workflow Pattern Phase 3 — Per-validator missing-payload entry returned
/// in the 409 envelope when a promote-status / workflow-complete / jump
/// request is rejected because readiness gates aren't satisfied.
///
/// <para>
/// <see cref="BlockingStepId"/> / <see cref="BlockingStepLabelKey"/> are
/// populated when the missing validator can be tied to a specific step of
/// the active workflow definition (jump-ahead, complete, promote-with-run).
/// They stay null when the missing validator isn't bound to a step (e.g. a
/// standalone promote on an entity with no in-flight run). Callers use
/// these to render "Finish 'Sourcing' first" instead of the generic
/// "An earlier step is incomplete."
/// </para>
/// </summary>
public record MissingValidatorResponseModel(
    string ValidatorId,
    string DisplayNameKey,
    string MissingMessageKey,
    string? BlockingStepId = null,
    string? BlockingStepLabelKey = null);
