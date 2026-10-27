namespace Forge.Core.Models;

public record EmployeeTerminationStatusModel(
    DateOnly TerminationDate,
    string ReasonCode,
    string? Notes,
    bool RehireEligible,
    bool FinalPayRequired,
    DateTimeOffset? FinalPayProcessedAt,
    DateTimeOffset? AppliedAt,
    int? TerminatedByUserId);
