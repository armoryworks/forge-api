using FluentValidation;
using FluentValidation.Results;

using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Scheduling;

public static class WorkCenterTeamGuard
{
    public static async Task<string?> ResolveTeamNameAsync(AppDbContext db, int? teamId, CancellationToken ct)
    {
        if (teamId is not int id)
            return null;

        var name = await db.Teams
            .Where(t => t.Id == id && t.IsActive)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct);
        if (name is not null)
            return name;

        throw new ValidationException(new[]
        {
            new ValidationFailure("teamId", "That team no longer exists. Choose another team.") { AttemptedValue = teamId },
        });
    }
}
