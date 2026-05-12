using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

public record KioskLoginCommand(string Barcode, string Pin) : IRequest<LoginResponse>;

public class KioskLoginValidator : AbstractValidator<KioskLoginCommand>
{
    public KioskLoginValidator()
    {
        RuleFor(x => x.Barcode).NotEmpty();
        RuleFor(x => x.Pin).NotEmpty();
    }
}

public class KioskLoginHandler(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    ISessionStore sessionStore,
    IHttpContextAccessor httpContext,
    IRoleClaimsExpander roleClaimsExpander) : IRequestHandler<KioskLoginCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(KioskLoginCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.Users
            .FirstOrDefaultAsync(u => u.EmployeeBarcode == request.Barcode && u.IsActive, cancellationToken);

        if (user == null || string.IsNullOrEmpty(user.PinHash))
            throw new InvalidOperationException("Invalid barcode or PIN");

        if (!SetPinHandler.VerifyPin(request.Pin, user.PinHash))
            throw new InvalidOperationException("Invalid barcode or PIN");

        // WU-06 / C1 — RoleTemplate expansion on kiosk login.
        var roles = await roleClaimsExpander.GetEffectiveRolesAsync(user, cancellationToken);
        var result = tokenService.GenerateToken(
            user.Id, user.Email!, user.FirstName, user.LastName,
            user.Initials, user.AvatarColor, roles,
            TimeSpan.FromHours(8));

        await sessionStore.CreateSessionAsync(user.Id, result.Jti, result.ExpiresAt,
            "kiosk",
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
