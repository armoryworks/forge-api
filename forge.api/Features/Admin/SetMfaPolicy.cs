using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Api.Features.Admin;

public record SetMfaPolicyCommand(IReadOnlyList<string> RequiredRoles) : IRequest;

public class SetMfaPolicyHandler(AppDbContext db, UserManager<ApplicationUser> userManager) : IRequestHandler<SetMfaPolicyCommand>
{
    public async Task Handle(SetMfaPolicyCommand request, CancellationToken cancellationToken)
    {
        await MfaRolePolicy.SetRequiredRolesAsync(db, request.RequiredRoles, cancellationToken);

        var users = await db.Users.ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            var enforced = MfaRolePolicy.AppliesTo(request.RequiredRoles, user, roles);
            user.MfaEnforcedByPolicy = enforced;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
