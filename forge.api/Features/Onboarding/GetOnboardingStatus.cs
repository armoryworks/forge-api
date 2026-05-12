using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Onboarding;

public record GetOnboardingStatusQuery(int UserId) : IRequest<OnboardingStatusModel>;

public class GetOnboardingStatusHandler(AppDbContext db)
    : IRequestHandler<GetOnboardingStatusQuery, OnboardingStatusModel>
{
    public async Task<OnboardingStatusModel> Handle(
        GetOnboardingStatusQuery request, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == request.UserId, ct);

        // Bypass flag treats all items as complete
        if (profile?.OnboardingBypassedAt is not null)
            return new OnboardingStatusModel(
                W4Complete: true, I9Complete: true, StateWithholdingComplete: true,
                DirectDepositComplete: true, WorkersCompComplete: true, HandbookComplete: true,
                AllComplete: true, CanBeAssigned: true);

        var w4 = profile?.W4CompletedAt is not null;
        var i9 = profile?.I9CompletedAt is not null;
        var state = profile?.StateWithholdingCompletedAt is not null;
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
            CanBeAssigned: w4 && i9 && state);
    }
}
