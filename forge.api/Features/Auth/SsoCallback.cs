using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

public record SsoCallbackCommand(string Provider, string ExternalId, string Email) : IRequest<LoginResponse>;

public class SsoCallbackHandler(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    ISessionStore sessionStore,
    IHttpContextAccessor httpContext,
    IRoleClaimsExpander roleClaimsExpander) : IRequestHandler<SsoCallbackCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(SsoCallbackCommand request, CancellationToken cancellationToken)
    {
        // Find user by SSO identity link
        ApplicationUser? user = request.Provider switch
        {
            "google" => await userManager.Users.FirstOrDefaultAsync(
                u => u.GoogleId == request.ExternalId && u.IsActive, cancellationToken),
            "microsoft" => await userManager.Users.FirstOrDefaultAsync(
                u => u.MicrosoftId == request.ExternalId && u.IsActive, cancellationToken),
            "oidc" => await userManager.Users.FirstOrDefaultAsync(
                u => u.OidcSubjectId == request.ExternalId && u.IsActive, cancellationToken),
            _ => null
        };

        // If no linked account, try to find by email (auto-link on first SSO login)
        if (user == null)
        {
            user = await userManager.Users.FirstOrDefaultAsync(
                u => u.Email == request.Email && u.IsActive, cancellationToken);

            if (user == null)
                throw new InvalidOperationException("No account found. Contact your administrator to create an account first.");

            // Auto-link the SSO identity
            switch (request.Provider)
            {
                case "google": user.GoogleId = request.ExternalId; break;
                case "microsoft": user.MicrosoftId = request.ExternalId; break;
                case "oidc":
                    user.OidcSubjectId = request.ExternalId;
                    user.OidcProvider = request.Provider;
                    break;
            }

            await userManager.UpdateAsync(user);
        }

        // WU-06 / C1 — RoleTemplate expansion on SSO login.
        var roles = await roleClaimsExpander.GetEffectiveRolesAsync(user, cancellationToken);
        var result = tokenService.GenerateToken(
            user.Id, user.Email!, user.FirstName, user.LastName,
            user.Initials, user.AvatarColor, roles);

        await sessionStore.CreateSessionAsync(user.Id, result.Jti, result.ExpiresAt,
            $"sso:{request.Provider}",
            httpContext.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            httpContext.HttpContext?.Request.Headers.UserAgent.ToString(),
            cancellationToken);

        return new LoginResponse(
            result.Token,
            result.ExpiresAt,
            new AuthUserResponseModel(
                user.Id, user.Email!, user.FirstName, user.LastName,
                user.Initials, user.AvatarColor, roles.ToArray(), false));
    }
}
