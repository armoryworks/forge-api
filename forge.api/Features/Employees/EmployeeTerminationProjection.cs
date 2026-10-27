using Forge.Core.Models;

namespace Forge.Api.Features.Employees;

public static class EmployeeTerminationProjection
{
    public static EmployeeTerminationStatusModel? From(Core.Entities.EmployeeProfile? profile)
        => profile?.TerminationDate is DateOnly date
            ? new EmployeeTerminationStatusModel(
                date,
                profile.TerminationReasonCode ?? string.Empty,
                profile.TerminationNotes,
                profile.RehireEligible ?? false,
                profile.FinalPayRequired ?? false,
                profile.FinalPayProcessedAt,
                profile.TerminationAppliedAt,
                profile.TerminatedByUserId)
            : null;
}
