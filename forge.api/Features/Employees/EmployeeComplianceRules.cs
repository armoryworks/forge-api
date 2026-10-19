using Forge.Core.Models;

namespace Forge.Api.Features.Employees;

/// <summary>
/// The one definition of an employee's onboarding compliance checklist, shared by the
/// detail header, the stats card and the compliance tab so all three agree.
/// </summary>
public static class EmployeeComplianceRules
{
    public static IReadOnlyList<EmployeeComplianceCheckModel> Checklist(Core.Entities.EmployeeProfile? profile) =>
    [
        new("w4", profile?.W4CompletedAt is not null),
        new("i9", profile?.I9CompletedAt is not null),
        new("stateWithholding", profile?.StateWithholdingCompletedAt is not null),
        new("emergencyContact", profile is not null &&
            !string.IsNullOrWhiteSpace(profile.EmergencyContactName) &&
            !string.IsNullOrWhiteSpace(profile.EmergencyContactPhone)),
        new("address", profile is not null &&
            !string.IsNullOrWhiteSpace(profile.Street1) &&
            !string.IsNullOrWhiteSpace(profile.City) &&
            !string.IsNullOrWhiteSpace(profile.State) &&
            !string.IsNullOrWhiteSpace(profile.ZipCode)),
        new("directDeposit", profile?.DirectDepositCompletedAt is not null),
        new("workersComp", profile?.WorkersCompAcknowledgedAt is not null),
        new("handbook", profile?.HandbookAcknowledgedAt is not null),
    ];

    public static int PercentComplete(IReadOnlyList<EmployeeComplianceCheckModel> checklist) =>
        checklist.Count > 0
            ? (int)Math.Round(100.0 * checklist.Count(c => c.Completed) / checklist.Count)
            : 0;
}
