using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Middleware;

/// <summary>
/// Validates the X-Device-Token a shared mobile device sends on every
/// request: unknown or revoked → 401 with code "device-revoked" (the app
/// wipes); valid → the device id rides in HttpContext.Items for
/// attribution and last-seen is touched at most every five minutes.
/// </summary>
public class SharedDeviceMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Device-Token";
    public const string ItemKey = "SharedDeviceId";
    private static readonly TimeSpan LastSeenThrottle = TimeSpan.FromMinutes(5);

    public async Task InvokeAsync(HttpContext context, AppDbContext db, IClock clock)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var values)
            || string.IsNullOrWhiteSpace(values))
        {
            await next(context);
            return;
        }

        var hash = OpaqueTokens.Sha256Hex(values.ToString().Trim());
        var device = await db.UserDevices
            .Where(d => d.DeviceTokenHash == hash && d.IsShared)
            .Select(d => new { d.Id, d.RevokedAt, d.LastSeenAt })
            .FirstOrDefaultAsync(context.RequestAborted);

        if (device is null || device.RevokedAt is not null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                status = 401,
                title = "Device revoked",
                detail = "This device's access has been revoked.",
                type = "about:blank",
                code = "device-revoked",
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return;
        }

        context.Items[ItemKey] = device.Id;

        var now = clock.UtcNow;
        if (device.LastSeenAt is null || now - device.LastSeenAt > LastSeenThrottle)
        {
            await db.UserDevices.Where(d => d.Id == device.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now), context.RequestAborted);
        }

        await next(context);
    }
}
