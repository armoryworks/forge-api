using System.Security.Claims;
using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Auth;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Devices;

public record EnrollOwnDeviceCommand(
    string DeviceUuid,
    string DeviceName,
    string Platform,
    string? OsVersion,
    string? AppVersion) : IRequest<MobileAuthResponseModel>;

public class EnrollOwnDeviceValidator : AbstractValidator<EnrollOwnDeviceCommand>
{
    public EnrollOwnDeviceValidator()
    {
        RuleFor(x => x.DeviceUuid).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DeviceName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Platform).NotEmpty().Must(p => p is "ios" or "android")
            .WithMessage("Platform must be 'ios' or 'android'.");
        RuleFor(x => x.OsVersion).MaximumLength(50);
        RuleFor(x => x.AppVersion).MaximumLength(30);
    }
}

/// <summary>
/// Manual-path enrollment: the caller holds a full access token from a
/// normal login on the phone — which by definition satisfied the instance's
/// second-factor policy — and enrolls the device they are holding. Issues
/// the refresh family plus a fresh access token bound to the device.
/// </summary>
public class EnrollOwnDeviceHandler(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    ISessionStore sessionStore,
    IRoleClaimsExpander roleClaimsExpander,
    IDeviceCredentialService deviceCredentials,
    IHttpContextAccessor httpContext,
    ISystemAuditWriter auditWriter)
    : IRequestHandler<EnrollOwnDeviceCommand, MobileAuthResponseModel>
{
    public async Task<MobileAuthResponseModel> Handle(
        EnrollOwnDeviceCommand request, CancellationToken cancellationToken)
    {
        var http = httpContext.HttpContext!;
        var userId = int.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
            throw new UnauthorizedAccessException("Account is disabled.");

        var credential = await deviceCredentials.MintAsync(
            userId, request.DeviceUuid, request.DeviceName, request.Platform,
            request.OsVersion, request.AppVersion,
            enrolledByUserId: userId, cancellationToken);
        var device = credential.Device;

        var roles = await roleClaimsExpander.GetEffectiveRolesAsync(user, cancellationToken);
        var access = tokenService.GenerateToken(
            user.Id, user.Email!, user.FirstName, user.LastName,
            user.Initials, user.AvatarColor, roles);

        await sessionStore.CreateSessionAsync(user.Id, access.Jti, access.ExpiresAt,
            authMethod: "mobile-enroll-manual",
            ipAddress: http.Connection.RemoteIpAddress?.ToString(),
            userAgent: http.Request.Headers.UserAgent.ToString(),
            ct: cancellationToken);
        await db.UserSessions
            .Where(s => s.Jti == access.Jti)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserDeviceId, device.Id),
                cancellationToken);

        await auditWriter.WriteAsync(
            DeviceAuditEvents.Enrolled, user.Id,
            entityType: DeviceAuditEvents.EntityType, entityId: device.Id,
            details: JsonSerializer.Serialize(new
            {
                device.DeviceUuid,
                device.Platform,
                path = "manual",
            }),
            ct: cancellationToken);

        return new MobileAuthResponseModel(
            access.Token, access.ExpiresAt,
            credential.RawRefreshToken, credential.RefreshTokenExpiresAt,
            device.Id, device.Name,
            new AuthUserResponseModel(
                user.Id, user.Email!, user.FirstName, user.LastName,
                user.Initials, user.AvatarColor, roles.ToArray(), false));
    }
}
