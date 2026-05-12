using Forge.Core.Models;

namespace Forge.Api.Workflows;

/// <summary>
/// Workflow Pattern Phase 3 — Thrown when a step-advance / promote / complete
/// request is rejected because readiness validators aren't satisfied. The
/// global exception middleware translates this to a 409 Conflict with the
/// missing-validators envelope.
/// </summary>
public class WorkflowMissingValidatorsException(
    IReadOnlyList<MissingValidatorResponseModel> missing,
    string message) : InvalidOperationException(message)
{
    public IReadOnlyList<MissingValidatorResponseModel> Missing { get; } = missing;
}
