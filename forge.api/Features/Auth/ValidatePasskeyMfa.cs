using Fido2NetLib;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

public record ValidatePasskeyMfaCommand(
    string MfaPendingToken,
    string Origin,
    AuthenticatorAssertionRawResponse Response) : IRequest<MfaValidateResponseModel?>;

public class ValidatePasskeyMfaValidator : AbstractValidator<ValidatePasskeyMfaCommand>
{
    public ValidatePasskeyMfaValidator()
    {
        RuleFor(x => x.MfaPendingToken).NotEmpty();
        RuleFor(x => x.Response).NotNull();
    }
}

/// <summary>
/// Verifies the passkey assertion for the pending login and, on success,
/// issues the full session token — the passkey twin of the TOTP validate
/// endpoint. Returns null on a failed assertion.
/// </summary>
public class ValidatePasskeyMfaHandler(
    IPasskeyService passkeys,
    IMfaPreAuthTokenService preAuth,
    UserManager<ApplicationUser> userManager,
    IRoleClaimsExpander roleClaimsExpander,
    ITokenService tokenService,
    ISessionStore sessionStore,
    IHttpContextAccessor httpContext)
    : IRequestHandler<ValidatePasskeyMfaCommand, MfaValidateResponseModel?>
{
    public async Task<MfaValidateResponseModel?> Handle(
        ValidatePasskeyMfaCommand request, CancellationToken cancellationToken)
    {
        var userId = preAuth.ValidateAndGetUserId(request.MfaPendingToken)
            ?? throw new UnauthorizedAccessException("Invalid or expired MFA session");

        var verified = await passkeys.CompleteAssertionAsync(
            userId, request.Origin, request.Response, cancellationToken);
        if (!verified) return null;

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
            throw new UnauthorizedAccessException("Account is disabled.");

        var roles = await roleClaimsExpander.GetEffectiveRolesAsync(user, cancellationToken);
        var result = tokenService.GenerateToken(
            user.Id, user.Email!, user.FirstName, user.LastName,
            user.Initials, user.AvatarColor, roles);

        var http = httpContext.HttpContext;
        await sessionStore.CreateSessionAsync(user.Id, result.Jti, result.ExpiresAt,
            authMethod: "mfa-passkey",
            ipAddress: http?.Connection.RemoteIpAddress?.ToString(),
            userAgent: http?.Request.Headers.UserAgent.ToString(),
            ct: cancellationToken);

        return new MfaValidateResponseModel
        {
            AccessToken = result.Token,
            ExpiresAt = result.ExpiresAt,
        };
    }
}
