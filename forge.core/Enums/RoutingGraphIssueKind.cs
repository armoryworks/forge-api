namespace Forge.Core.Enums;

public enum RoutingGraphIssueKind
{
    MissingStep,
    UnknownStep,
    DuplicateStep,
    InvalidStepNumber,
    SelfLink,
    UnknownPredecessor,
    PredecessorNotEarlier,
    TooManyPredecessors,
}
