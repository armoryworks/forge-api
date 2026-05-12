using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public static class AssigneeComplianceCheck
{
    public static async Task EnsureCanBeAssigned(AppDbContext db, int userId, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        // Allow assignment if the user has bypassed onboarding (e.g., skipped on mobile)
        var onboardingBypassed = profile?.OnboardingBypassedAt is not null;

        var canBeAssigned = profile is not null &&
            (onboardingBypassed || (
                profile.W4CompletedAt is not null &&
                profile.I9CompletedAt is not null &&
                profile.StateWithholdingCompletedAt is not null &&
                !string.IsNullOrWhiteSpace(profile.EmergencyContactName) &&
                !string.IsNullOrWhiteSpace(profile.EmergencyContactPhone)));

        if (!canBeAssigned)
        {
            throw new InvalidOperationException(
                "User cannot be assigned to jobs — required compliance documents are incomplete " +
                "(W-4, I-9, State Withholding, or Emergency Contact).");
        }
    }
}
