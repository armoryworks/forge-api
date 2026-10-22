using Forge.Core.Enums;

namespace Forge.Core.Models;

public record StepGateViolationResponseModel(
    StepGatingMode Mode,
    PredecessorGate Gate,
    int? PredecessorStep,
    decimal? Available,
    decimal Requested,
    List<int> WaitingOnSteps,
    bool CanOverride);
