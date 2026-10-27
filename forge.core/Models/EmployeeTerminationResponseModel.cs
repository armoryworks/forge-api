namespace Forge.Core.Models;

public record EmployeeTerminationResponseModel(
    int EmployeeProfileId,
    int? UserId,
    DateOnly TerminationDate,
    string ReasonCode,
    string? Notes,
    bool RehireEligible,
    bool FinalPayRequired,
    DateTimeOffset? AppliedAt,
    TerminationEffectsModel? Effects);
