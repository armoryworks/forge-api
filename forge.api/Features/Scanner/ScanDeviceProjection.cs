using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scanner;

public static class ScanDeviceProjection
{
    public static async Task<List<ScanDeviceResponseModel>> ToResponsesAsync(
        AppDbContext db, IReadOnlyCollection<UserScanDevice> devices, CancellationToken ct)
    {
        var userIds = devices.Select(d => d.UserId)
            .Concat(devices.Where(d => d.AssignedUserId.HasValue).Select(d => d.AssignedUserId!.Value))
            .Distinct()
            .ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.LastName, u.FirstName })
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}".Trim(',', ' '), ct);

        var deviceIds = devices.Select(d => d.DeviceId).ToList();
        var lastSeen = await db.ScanActionLogs.AsNoTracking()
            .Where(l => l.DeviceId != null && deviceIds.Contains(l.DeviceId))
            .GroupBy(l => l.DeviceId!)
            .Select(g => new { DeviceId = g.Key, LastSeen = g.Max(l => l.CreatedAt) })
            .ToDictionaryAsync(x => x.DeviceId, x => x.LastSeen, ct);

        return devices.Select(d => new ScanDeviceResponseModel(
                d.Id,
                d.DeviceId,
                d.DeviceName,
                d.PairedAt,
                d.IsActive,
                d.AssignedUserId,
                d.AssignedUserId is int assigned ? names.GetValueOrDefault(assigned) : null,
                names.GetValueOrDefault(d.UserId),
                lastSeen.TryGetValue(d.DeviceId, out var seen) ? seen : null))
            .ToList();
    }

    public static async Task EnsureAssignableAsync(AppDbContext db, int? userId, CancellationToken ct)
    {
        if (userId is not int id)
            return;

        if (!await db.Users.AnyAsync(u => u.Id == id && u.IsActive, ct))
            throw new KeyNotFoundException($"User {id} not found or inactive");
    }
}
