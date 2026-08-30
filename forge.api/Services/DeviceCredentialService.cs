using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

public class DeviceCredentialService(
    AppDbContext db,
    IClock clock,
    IOptions<MobileOptions> options) : IDeviceCredentialService
{
    public async Task<DeviceCredential> MintAsync(
        int userId,
        string deviceUuid,
        string deviceName,
        string platform,
        string? osVersion,
        string? appVersion,
        int enrolledByUserId,
        CancellationToken ct)
    {
        var now = clock.UtcNow;

        // Re-enrollment with a known UUID reclaims the row (fresh family,
        // flags cleared) — the caller has already authorized the enrollment.
        var device = await db.UserDevices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.DeviceUuid == deviceUuid, ct);

        if (device is null)
        {
            device = new UserDevice { DeviceUuid = deviceUuid };
            db.UserDevices.Add(device);
        }

        device.UserId = userId;
        device.Name = deviceName;
        device.Platform = platform;
        device.OsVersion = osVersion;
        device.AppVersion = appVersion;
        device.IsShared = false;
        device.EnrolledByUserId = enrolledByUserId;
        device.LastSeenAt = now;
        device.RevokedAt = null;
        device.RevokedByUserId = null;
        device.IsFlagged = false;
        device.DeletedAt = null;
        device.DeletedBy = null;
        await db.SaveChangesAsync(ct);

        await db.DeviceRefreshTokens
            .Where(t => t.UserDeviceId == device.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);

        var rawRefresh = OpaqueTokens.NewToken();
        var refreshExpiresAt = now.AddDays(options.Value.RefreshTokenLifetimeDays);
        db.DeviceRefreshTokens.Add(new DeviceRefreshToken
        {
            UserDeviceId = device.Id,
            UserId = userId,
            FamilyId = Guid.NewGuid(),
            TokenHash = OpaqueTokens.Sha256Hex(rawRefresh),
            IssuedAt = now,
            ExpiresAt = refreshExpiresAt,
        });
        await db.SaveChangesAsync(ct);

        return new DeviceCredential(device, rawRefresh, refreshExpiresAt);
    }
}
