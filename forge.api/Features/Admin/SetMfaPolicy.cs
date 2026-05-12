using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record SetMfaPolicyCommand(IReadOnlyList<string> RequiredRoles) : IRequest;

public class SetMfaPolicyHandler(AppDbContext db, UserManager<ApplicationUser> userManager) : IRequestHandler<SetMfaPolicyCommand>
{
    public async Task Handle(SetMfaPolicyCommand request, CancellationToken cancellationToken)
    {
        var users = await db.Users.ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            var enforced = request.RequiredRoles.Any(r => roles.Contains(r));
            user.MfaEnforcedByPolicy = enforced;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
