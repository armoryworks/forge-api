using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Onboarding;

public record BypassOnboardingCommand(int UserId) : IRequest;

public class BypassOnboardingHandler(AppDbContext db) : IRequestHandler<BypassOnboardingCommand>
{
    public async Task Handle(BypassOnboardingCommand request, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles
            .FirstOrDefaultAsync(p => p.UserId == request.UserId, ct);

        if (profile is null)
        {
            profile = new Forge.Core.Entities.EmployeeProfile { UserId = request.UserId };
            db.EmployeeProfiles.Add(profile);
        }

        profile.OnboardingBypassedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
