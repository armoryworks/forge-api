using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

public record CompleteSetupCommand(
    string Token,
    string Password,
    string? FirstName,
    string? LastName) : IRequest<LoginResponse>;

public class CompleteSetupValidator : AbstractValidator<CompleteSetupCommand>
{
    public CompleteSetupValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}

public class CompleteSetupHandler(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    ISessionStore sessionStore,
    IHttpContextAccessor httpContext,
    IRoleClaimsExpander roleClaimsExpander) : IRequestHandler<CompleteSetupCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(CompleteSetupCommand request, CancellationToken cancellationToken)
    {
        var normalizedToken = request.Token.Trim().ToUpperInvariant();
        var users = userManager.Users
            .Where(u => u.SetupToken == normalizedToken && u.SetupTokenExpiresAt > DateTimeOffset.UtcNow);

        var user = users.FirstOrDefault()
            ?? throw new InvalidOperationException("Invalid or expired setup token");

        // Set password
        var removeResult = await userManager.RemovePasswordAsync(user);
        if (!removeResult.Succeeded && user.PasswordHash != null)
            throw new InvalidOperationException("Failed to reset password");

        var addResult = await userManager.AddPasswordAsync(user, request.Password);
        if (!addResult.Succeeded)
        {
            var errors = string.Join("; ", addResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Password requirements not met: {errors}");
        }

        // Update profile if provided
        if (!string.IsNullOrEmpty(request.FirstName))
            user.FirstName = request.FirstName;
        if (!string.IsNullOrEmpty(request.LastName))
            user.LastName = request.LastName;

        // Clear setup token
        user.SetupToken = null;
        user.SetupTokenExpiresAt = null;
        user.EmailConfirmed = true;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await userManager.UpdateAsync(user);

        // Generate JWT and return login response
        // WU-06 / C1 — RoleTemplate expansion at setup completion.
        var roles = await roleClaimsExpander.GetEffectiveRolesAsync(user, cancellationToken);
        var result = tokenService.GenerateToken(
            user.Id, user.Email!, user.FirstName, user.LastName,
            user.Initials, user.AvatarColor, roles);

        await sessionStore.CreateSessionAsync(user.Id, result.Jti, result.ExpiresAt,
            "setup",
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
