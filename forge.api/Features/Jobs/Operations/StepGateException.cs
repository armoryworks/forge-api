using Forge.Core.Models;

namespace Forge.Api.Features.Jobs.Operations;

/// <summary>
/// Thrown when step gating stops a progress update: the step would report more pieces than its predecessors
/// have completed, or be marked done before a Complete-gated predecessor. The exception middleware maps this to
/// 409 with problem code "step-gate" and the details in "gate"; the client shows them and, for Warn or an
/// overridable Block, resends with AcknowledgeGate=true and an OverrideReason.
/// </summary>
public class StepGateException(string message, StepGateViolationResponseModel violation) : Exception(message)
{
    public const string Code = "step-gate";

    public StepGateViolationResponseModel Violation { get; } = violation;
}
