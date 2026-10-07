using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.EmployeeProfile;

public record GetProfileCompletenessQuery(int UserId) : IRequest<ProfileCompletenessResponseModel>;

public class GetProfileCompletenessHandler(AppDbContext db, ICapabilitySnapshotProvider capabilities) : IRequestHandler<GetProfileCompletenessQuery, ProfileCompletenessResponseModel>
{
    public async Task<ProfileCompletenessResponseModel> Handle(GetProfileCompletenessQuery request, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == request.UserId, ct);

        var stateInfo = await EmployeeComplianceRules.ResolveStateWithholdingInfoAsync(db, request.UserId, ct);

        if (!EmployeeComplianceRules.IsHrTrackingOn(capabilities) || profile?.OnboardingBypassedAt is not null)
        {
            return new ProfileCompletenessResponseModel(
                IsComplete: true,
                CanBeAssignedJobs: true,
                TotalItems: 8,
                CompletedItems: 8,
                Items: [],
                StateWithholdingInfo: stateInfo);
        }

        var isNoTaxState = EmployeeComplianceRules.IsNoTaxState(stateInfo?.Category);

        var items = new List<ProfileCompletenessItem>
        {
            // Job-assignment blockers — must be complete before user can be assigned work
            new("w4", "W-4 Federal Tax Withholding",
                profile?.W4CompletedAt is not null,
                BlocksJobAssignment: true),

            new("i9", "I-9 Employment Eligibility",
                profile?.I9CompletedAt is not null,
                BlocksJobAssignment: true),

            new("stateWithholding",
                stateInfo is not null
                    ? $"State Tax Withholding ({stateInfo.StateName})"
                    : "State Tax Withholding",
                EmployeeComplianceRules.IsStateWithholdingSatisfied(profile, stateInfo?.Category),
                BlocksJobAssignment: !isNoTaxState),

            new("emergency_contact", "Emergency Contact",
                EmployeeComplianceRules.HasEmergencyContact(profile),
                BlocksJobAssignment: true),

            // Required but do not block job assignment
            new("address", "Home Address",
                EmployeeComplianceRules.HasHomeAddress(profile),
                BlocksJobAssignment: false),

            new("directDeposit", "Direct Deposit Authorization",
                profile?.DirectDepositCompletedAt is not null,
                BlocksJobAssignment: false),

            new("workersComp", "Workers' Comp Acknowledgment",
                profile?.WorkersCompAcknowledgedAt is not null,
                BlocksJobAssignment: false),

            new("handbook", "Employee Handbook Acknowledgment",
                profile?.HandbookAcknowledgedAt is not null,
                BlocksJobAssignment: false),
        };

        var completedCount = items.Count(i => i.IsComplete);
        var canBeAssigned = EmployeeComplianceRules.CanBeAssignedJobs(profile, stateInfo?.Category, hrOn: true);

        return new ProfileCompletenessResponseModel(
            IsComplete: completedCount == items.Count,
            CanBeAssignedJobs: canBeAssigned,
            TotalItems: items.Count,
            CompletedItems: completedCount,
            Items: items,
            StateWithholdingInfo: stateInfo);
    }
}
