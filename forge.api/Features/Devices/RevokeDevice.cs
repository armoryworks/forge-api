using System.Security.Claims;
using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Devices;

public record RevokeDeviceCommand(int DeviceId) : IRequest;

/// <summary>
/// Revokes a device: refresh family dead, live sessions dead, device marked.
/// The next contact from the device receives the device-revoked signal and
/// wipes this instance's data. Admins may revoke any device; a user may
/// revoke their own ("Sign out this device").
/// </summary>
public class RevokeDeviceHandler(
    AppDbContext db,
    ISessionStore sessionStore,
    IClock clock,
    IHttpContextAccessor httpContext,
    ISystemAuditWriter auditWriter)
    : IRequestHandler<RevokeDeviceCommand>
{
    public async Task Handle(RevokeDeviceCommand request, CancellationToken cancellationToken)
    {
        var principal = httpContext.HttpContext!.User;
        var actorId = int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = principal.IsInRole("Admin");

        var device = await db.UserDevices
            .FirstOrDefaultAsync(d => d.Id == request.DeviceId, cancellationToken)
            ?? throw new KeyNotFoundException($"Device {request.DeviceId} not found");

        if (!isAdmin && device.UserId != actorId)
            throw new UnauthorizedAccessException("Not your device.");

        var now = clock.UtcNow;

        if (device.RevokedAt is null)
        {
            device.RevokedAt = now;
            device.RevokedByUserId = actorId;
            await db.SaveChangesAsync(cancellationToken);
        }

        await db.DeviceRefreshTokens
            .Where(t => t.UserDeviceId == device.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);

        var jtis = await db.UserSessions
            .Where(s => s.UserDeviceId == device.Id && s.RevokedAt == null)
            .Select(s => s.Jti)
            .ToListAsync(cancellationToken);
        foreach (var jti in jtis)
            await sessionStore.RevokeSessionAsync(jti, cancellationToken);

        await auditWriter.WriteAsync(
            DeviceAuditEvents.Revoked, actorId,
            entityType: DeviceAuditEvents.EntityType, entityId: device.Id,
            details: JsonSerializer.Serialize(new { device.DeviceUuid, ownerUserId = device.UserId }),
            ct: cancellationToken);
    }
}
