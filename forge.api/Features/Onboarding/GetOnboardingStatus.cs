using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.EmployeeProfile;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Onboarding;

public record GetOnboardingStatusQuery(int UserId) : IRequest<OnboardingStatusModel>;

public class GetOnboardingStatusHandler(AppDbContext db, ICapabilitySnapshotProvider capabilities)
    : IRequestHandler<GetOnboardingStatusQuery, OnboardingStatusModel>
{
    private static readonly OnboardingStatusModel AllComplete = new(
        W4Complete: true, I9Complete: true, StateWithholdingComplete: true,
        DirectDepositComplete: true, WorkersCompComplete: true, HandbookComplete: true,
        AllComplete: true, CanBeAssigned: true);

    public async Task<OnboardingStatusModel> Handle(
        GetOnboardingStatusQuery request, CancellationToken ct)
    {
        if (!EmployeeComplianceRules.IsHrTrackingOn(capabilities))
            return AllComplete;

        var isNonEmployee = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == request.UserId)
            .Select(u => u.IsNonEmployee)
            .FirstOrDefaultAsync(ct);

        if (isNonEmployee)
            return AllComplete;

        var profile = await db.EmployeeProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == request.UserId, ct);

        if (profile?.OnboardingBypassedAt is not null)
            return AllComplete;

        var stateCategory = await EmployeeComplianceRules.ResolveStateCategoryAsync(db, request.UserId, ct);

        var w4 = profile?.W4CompletedAt is not null;
        var i9 = profile?.I9CompletedAt is not null;
        var state = EmployeeComplianceRules.IsStateWithholdingSatisfied(profile, stateCategory);
        var dd = profile?.DirectDepositCompletedAt is not null;
        var wc = profile?.WorkersCompAcknowledgedAt is not null;
        var hb = profile?.HandbookAcknowledgedAt is not null;

        return new OnboardingStatusModel(
            W4Complete: w4,
            I9Complete: i9,
            StateWithholdingComplete: state,
            DirectDepositComplete: dd,
            WorkersCompComplete: wc,
            HandbookComplete: hb,
            AllComplete: w4 && i9 && state && dd && wc && hb,
            CanBeAssigned: EmployeeComplianceRules.CanBeAssignedJobs(profile, stateCategory, hrOn: true));
    }
}
