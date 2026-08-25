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

public record RefreshDeviceTokenCommand(string RefreshToken, string DeviceUuid)
    : IRequest<MobileAuthResponseModel>;

public class RefreshDeviceTokenValidator : AbstractValidator<RefreshDeviceTokenCommand>
{
    public RefreshDeviceTokenValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceUuid).NotEmpty().MaximumLength(64);
    }
}

/// <summary>
/// Rotates a device refresh token: the presented token is consumed and its
/// successor issued in the same family. Presenting an already-consumed token
/// is treated as theft — the whole family is revoked and the device flagged.
/// A revoked device gets the device-revoked wipe signal.
/// </summary>
public class RefreshDeviceTokenHandler(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    ISessionStore sessionStore,
    IRoleClaimsExpander roleClaimsExpander,
    IClock clock,
    IOptions<MobileOptions> options,
    IHttpContextAccessor httpContext,
    ISystemAuditWriter auditWriter)
    : IRequestHandler<RefreshDeviceTokenCommand, MobileAuthResponseModel>
{
    public async Task<MobileAuthResponseModel> Handle(
        RefreshDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var hash = OpaqueTokens.Sha256Hex(request.RefreshToken);

        var token = await db.DeviceRefreshTokens
            .Include(t => t.UserDevice)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null || token.UserDevice.DeviceUuid != request.DeviceUuid)
            throw new UnauthorizedAccessException("Invalid refresh token.");

        var device = token.UserDevice;

        if (device.RevokedAt is not null || device.IsDeleted)
            throw new DeviceRevokedException("This device's access has been revoked.");

        if (token.RevokedAt is not null)
            throw new UnauthorizedAccessException("Invalid refresh token.");

        if (token.ConsumedAt is not null)
        {
            // Replay of a consumed token: someone is holding a stale copy.
            // Kill the family, flag the device, tell the audit log.
            await db.DeviceRefreshTokens
                .Where(t => t.FamilyId == token.FamilyId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);
            await db.UserDevices
                .Where(d => d.Id == device.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsFlagged, true), cancellationToken);

            await auditWriter.WriteAsync(
                DeviceAuditEvents.TokenReuseDetected, token.UserId,
                entityType: DeviceAuditEvents.EntityType, entityId: device.Id,
                details: JsonSerializer.Serialize(new { familyId = token.FamilyId }),
                ct: cancellationToken);

            throw new UnauthorizedAccessException("Invalid refresh token.");
        }

        if (token.ExpiresAt <= now)
            throw new UnauthorizedAccessException("Refresh token expired.");

        var user = await userManager.FindByIdAsync(token.UserId.ToString());
        if (user is null || !user.IsActive)
            throw new UnauthorizedAccessException("Account is disabled.");

        // Single-winner consume — a raced duplicate refresh loses cleanly
        // instead of minting two successors.
        var won = await db.DeviceRefreshTokens
            .Where(t => t.Id == token.Id && t.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAt, now), cancellationToken);
        if (won == 0)
            throw new UnauthorizedAccessException("Invalid refresh token.");

        var rawRefresh = OpaqueTokens.NewToken();
        var refreshExpiresAt = now.AddDays(options.Value.RefreshTokenLifetimeDays);
        db.DeviceRefreshTokens.Add(new DeviceRefreshToken
        {
            UserDeviceId = device.Id,
            UserId = token.UserId,
            FamilyId = token.FamilyId,
            TokenHash = OpaqueTokens.Sha256Hex(rawRefresh),
            IssuedAt = now,
            ExpiresAt = refreshExpiresAt,
        });

        device.LastSeenAt = now;
        await db.SaveChangesAsync(cancellationToken);

        // One live access-token session per device: retire the previous one.
        var oldJtis = await db.UserSessions
            .Where(s => s.UserDeviceId == device.Id && s.RevokedAt == null && s.ExpiresAt > now)
            .Select(s => s.Jti)
            .ToListAsync(cancellationToken);
        foreach (var oldJti in oldJtis)
            await sessionStore.RevokeSessionAsync(oldJti, cancellationToken);

        var roles = await roleClaimsExpander.GetEffectiveRolesAsync(user, cancellationToken);
        var access = tokenService.GenerateToken(
            user.Id, user.Email!, user.FirstName, user.LastName,
            user.Initials, user.AvatarColor, roles);

        var http = httpContext.HttpContext;
        await sessionStore.CreateSessionAsync(user.Id, access.Jti, access.ExpiresAt,
            authMethod: "mobile-refresh",
            ipAddress: http?.Connection.RemoteIpAddress?.ToString(),
            userAgent: http?.Request.Headers.UserAgent.ToString(),
            ct: cancellationToken);
        await db.UserSessions
            .Where(s => s.Jti == access.Jti)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserDeviceId, device.Id),
                cancellationToken);

        return new MobileAuthResponseModel(
            access.Token, access.ExpiresAt,
            rawRefresh, refreshExpiresAt,
            device.Id, device.Name,
            new AuthUserResponseModel(
                user.Id, user.Email!, user.FirstName, user.LastName,
                user.Initials, user.AvatarColor, roles.ToArray(), false));
    }
}
