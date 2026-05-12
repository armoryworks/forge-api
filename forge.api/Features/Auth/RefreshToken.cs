using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Identity;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

public record RefreshTokenCommand(string CurrentJti, int UserId) : IRequest<LoginResponse>;

public class RefreshTokenHandler(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    ISessionStore sessionStore,
    IRoleClaimsExpander roleClaimsExpander) : IRequestHandler<RefreshTokenCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        // Verify user still exists and is active
        var user = await userManager.FindByIdAsync(request.UserId.ToString())
            ?? throw new UnauthorizedAccessException("User not found");

        if (!user.IsActive)
            throw new UnauthorizedAccessException("Account is disabled");

        // WU-06 / C1 — expand RoleTemplate so refreshed JWT carries rollup roles.
        var roles = await roleClaimsExpander.GetEffectiveRolesAsync(user, cancellationToken);
        var result = tokenService.GenerateToken(
            user.Id, user.Email!, user.FirstName, user.LastName,
            user.Initials, user.AvatarColor, roles);

        // Rotate session JTI (atomically replaces old JTI with new one)
        var updated = await sessionStore.UpdateSessionJtiAsync(
            request.CurrentJti, result.Jti, result.ExpiresAt, cancellationToken);

        if (updated is null)
            throw new UnauthorizedAccessException("Session is no longer valid");

        return new LoginResponse(
            result.Token,
            result.ExpiresAt,
            new AuthUserResponseModel(
                user.Id, user.Email!, user.FirstName, user.LastName,
                user.Initials, user.AvatarColor, roles.ToArray(), false));
    }
}
