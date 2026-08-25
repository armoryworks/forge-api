using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Forge.Api.Features.Auth;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Devices;

public record EnrollDeviceCommand(
    string Token,
    string DeviceUuid,
    string DeviceName,
    string Platform,
    string? OsVersion,
    string? AppVersion) : IRequest<MobileAuthResponseModel>;

public class EnrollDeviceValidator : AbstractValidator<EnrollDeviceCommand>
{
    public EnrollDeviceValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceUuid).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DeviceName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Platform).NotEmpty().Must(p => p is "ios" or "android")
            .WithMessage("Platform must be 'ios' or 'android'.");
        RuleFor(x => x.OsVersion).MaximumLength(50);
        RuleFor(x => x.AppVersion).MaximumLength(30);
    }
}

/// <summary>
/// Exchanges an admin-issued one-time enrollment token for device
/// credentials: a device record, a rotating refresh-token family, and a
/// first access token. The QR that carried the token counts as the second
/// factor for this enrollment; afterwards the device plus local unlock is
/// the factor.
/// </summary>
public class EnrollDeviceHandler(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    ISessionStore sessionStore,
    IRoleClaimsExpander roleClaimsExpander,
    IClock clock,
    IOptions<MobileOptions> options,
    IHttpContextAccessor httpContext,
    ISystemAuditWriter auditWriter)
    : IRequestHandler<EnrollDeviceCommand, MobileAuthResponseModel>
{
    public async Task<MobileAuthResponseModel> Handle(
        EnrollDeviceCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var hash = OpaqueTokens.Sha256Hex(request.Token);

        var token = await db.DeviceEnrollmentTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null || token.ExpiresAt <= now || token.ConsumedAt is not null)
            throw new UnauthorizedAccessException("Enrollment code is invalid or expired.");

        if (token.IsShared || token.TargetUserId is null)
            throw new InvalidOperationException("Shared-device enrollment is not available yet.");

        var user = await userManager.FindByIdAsync(token.TargetUserId.Value.ToString());
        if (user is null || !user.IsActive)
            throw new UnauthorizedAccessException("Enrollment code is invalid or expired.");

        // Single-winner consume: a raced duplicate scan loses here.
        var won = await db.DeviceEnrollmentTokens
            .Where(t => t.Id == token.Id && t.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAt, now), cancellationToken);
        if (won == 0)
            throw new UnauthorizedAccessException("Enrollment code is invalid or expired.");

        // Re-enrollment with a known UUID reclaims the row (fresh family,
        // flags cleared) — the enrollment token authorizes it.
        var device = await db.UserDevices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.DeviceUuid == request.DeviceUuid, cancellationToken);

        if (device is null)
        {
            device = new UserDevice { DeviceUuid = request.DeviceUuid };
            db.UserDevices.Add(device);
        }

        device.UserId = user.Id;
        device.Name = request.DeviceName;
        device.Platform = request.Platform;
        device.OsVersion = request.OsVersion;
        device.AppVersion = request.AppVersion;
        device.IsShared = false;
        device.EnrolledByUserId = token.IssuedByUserId;
        device.LastSeenAt = now;
        device.RevokedAt = null;
        device.RevokedByUserId = null;
        device.IsFlagged = false;
        device.DeletedAt = null;
        device.DeletedBy = null;
        await db.SaveChangesAsync(cancellationToken);

        await db.DeviceEnrollmentTokens
            .Where(t => t.Id == token.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedByDeviceId, device.Id),
                cancellationToken);

        // Fresh refresh family. Any previous family for this device dies with
        // the re-enrollment.
        await db.DeviceRefreshTokens
            .Where(t => t.UserDeviceId == device.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);

        var rawRefresh = OpaqueTokens.NewToken();
        var refreshExpiresAt = now.AddDays(options.Value.RefreshTokenLifetimeDays);
        db.DeviceRefreshTokens.Add(new DeviceRefreshToken
        {
            UserDeviceId = device.Id,
            UserId = user.Id,
            FamilyId = Guid.NewGuid(),
            TokenHash = OpaqueTokens.Sha256Hex(rawRefresh),
            IssuedAt = now,
            ExpiresAt = refreshExpiresAt,
        });
        await db.SaveChangesAsync(cancellationToken);

        var roles = await roleClaimsExpander.GetEffectiveRolesAsync(user, cancellationToken);
        var access = tokenService.GenerateToken(
            user.Id, user.Email!, user.FirstName, user.LastName,
            user.Initials, user.AvatarColor, roles);

        var http = httpContext.HttpContext;
        await sessionStore.CreateSessionAsync(user.Id, access.Jti, access.ExpiresAt,
            authMethod: "mobile-enroll",
            ipAddress: http?.Connection.RemoteIpAddress?.ToString(),
            userAgent: http?.Request.Headers.UserAgent.ToString(),
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
                issuedBy = token.IssuedByUserId,
            }),
            ct: cancellationToken);

        return new MobileAuthResponseModel(
            access.Token, access.ExpiresAt,
            rawRefresh, refreshExpiresAt,
            device.Id, device.Name,
            new AuthUserResponseModel(
                user.Id, user.Email!, user.FirstName, user.LastName,
                user.Initials, user.AvatarColor, roles.ToArray(), false));
    }
}
