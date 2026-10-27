namespace Forge.Core.Models;

public record TerminateEmployeeRequestModel(
    DateOnly TerminationDate,
    string ReasonCode,
    string? Notes,
    bool RehireEligible,
    bool FinalPayRequired);
