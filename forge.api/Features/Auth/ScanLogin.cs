using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

/// <summary>
/// Unified scan authentication — accepts any scan identifier (RFID, NFC, barcode, biometric)
/// and resolves the user by checking UserScanIdentifiers first, then EmployeeBarcode fallback.
/// All scan types are handled by this single endpoint in parallel on the kiosk.
/// </summary>
public record ScanLoginCommand(string ScanValue, string Pin) : IRequest<LoginResponse>;

public class ScanLoginValidator : AbstractValidator<ScanLoginCommand>
{
    public ScanLoginValidator()
    {
        RuleFor(x => x.ScanValue).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Pin).NotEmpty().Matches(@"^\d{4,8}$")
            .WithMessage("PIN must be 4-8 digits.");
    }
}

public class ScanLoginHandler(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    ISessionStore sessionStore,
    IHttpContextAccessor httpContext,
    IRoleClaimsExpander roleClaimsExpander) : IRequestHandler<ScanLoginCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(ScanLoginCommand request, CancellationToken cancellationToken)
    {
        ApplicationUser? user = null;
        string authTier = "scan";

        // 1. Check UserScanIdentifiers table (RFID, NFC, barcode, biometric)
        var identifier = await db.UserScanIdentifiers
            .FirstOrDefaultAsync(x => x.IdentifierValue == request.ScanValue
                && x.IsActive, cancellationToken);

        if (identifier != null)
        {
            user = await userManager.FindByIdAsync(identifier.UserId.ToString());
            authTier = identifier.IdentifierType; // "rfid", "nfc", "barcode", "biometric"
        }

        // 2. Fallback: check ApplicationUser.EmployeeBarcode field
        if (user == null)
        {
            user = await userManager.Users
                .FirstOrDefaultAsync(u => u.EmployeeBarcode == request.ScanValue
                    && u.IsActive, cancellationToken);
            authTier = "barcode";
        }

        // 3. Validate user found and active
        if (user == null || !user.IsActive)
            throw new InvalidOperationException("Invalid scan identifier or PIN.");

        // 4. Verify PIN
        if (string.IsNullOrEmpty(user.PinHash))
            throw new InvalidOperationException("PIN not configured. Contact your administrator.");

        if (!SetPinHandler.VerifyPin(request.Pin, user.PinHash))
            throw new InvalidOperationException("Invalid scan identifier or PIN.");

        // 5. Generate JWT with auth tier claim
        // WU-06 / C1 — RoleTemplate expansion on scan login.
        var roles = await roleClaimsExpander.GetEffectiveRolesAsync(user, cancellationToken);
        var extraClaims = new Dictionary<string, string> { ["authTier"] = authTier };
        var result = tokenService.GenerateToken(
            user.Id, user.Email!, user.FirstName, user.LastName,
            user.Initials, user.AvatarColor, roles,
            TimeSpan.FromHours(8), extraClaims);

        await sessionStore.CreateSessionAsync(user.Id, result.Jti, result.ExpiresAt,
            authTier,
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
