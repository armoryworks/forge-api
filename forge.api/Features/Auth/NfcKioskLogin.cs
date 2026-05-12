using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

public record NfcKioskLoginCommand(string IdentifierValue, string Pin) : IRequest<LoginResponse>;

public class NfcKioskLoginValidator : AbstractValidator<NfcKioskLoginCommand>
{
    public NfcKioskLoginValidator()
    {
        RuleFor(x => x.IdentifierValue).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Pin).NotEmpty().Matches(@"^\d{4,8}$");
    }
}

public class NfcKioskLoginHandler(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    ISessionStore sessionStore,
    IHttpContextAccessor httpContext,
    IRoleClaimsExpander roleClaimsExpander) : IRequestHandler<NfcKioskLoginCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(NfcKioskLoginCommand request, CancellationToken cancellationToken)
    {
        var identifier = await db.UserScanIdentifiers
            .FirstOrDefaultAsync(x => x.IdentifierValue == request.IdentifierValue
                && x.IsActive, cancellationToken);

        if (identifier == null)
            throw new InvalidOperationException("Invalid scan identifier or PIN");

        var user = await userManager.FindByIdAsync(identifier.UserId.ToString());

        if (user == null || !user.IsActive)
            throw new InvalidOperationException("Invalid scan identifier or PIN");

        if (string.IsNullOrEmpty(user.PinHash))
            throw new InvalidOperationException("PIN not configured. Contact admin.");

        if (!SetPinHandler.VerifyPin(request.Pin, user.PinHash))
            throw new InvalidOperationException("Invalid scan identifier or PIN");

        // WU-06 / C1 — RoleTemplate expansion on NFC kiosk login.
        var roles = await roleClaimsExpander.GetEffectiveRolesAsync(user, cancellationToken);
        var extraClaims = new Dictionary<string, string> { ["authTier"] = "nfc" };
        var result = tokenService.GenerateToken(
            user.Id, user.Email!, user.FirstName, user.LastName,
            user.Initials, user.AvatarColor, roles,
            TimeSpan.FromHours(8), extraClaims);

        await sessionStore.CreateSessionAsync(user.Id, result.Jti, result.ExpiresAt,
            "nfc",
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
