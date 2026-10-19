using System.Net;
using System.Threading.RateLimiting;

namespace Forge.Api.Features.CustomerPortal;

/// <summary>
/// Per-IP limit on the anonymous portal sign-in endpoints (request-link,
/// exchange), on top of the global limiter, so a single client cannot
/// spray magic-link requests or guess tokens. Like the global limiter it
/// does not apply in Development or from loopback (E2E, local tools).
/// The client IP is the forwarded one, since UseForwardedHeaders runs
/// before UseRateLimiter.
/// </summary>
public static class PortalAuthRateLimit
{
    public const string PolicyName = "portal-auth";

    public const int PermitLimit = 10;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static RateLimitPartition<string> Partition(HttpContext ctx, bool isDevelopment)
    {
        if (isDevelopment)
            return RateLimitPartition.GetNoLimiter("dev");

        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is null || IPAddress.IsLoopback(ip))
            return RateLimitPartition.GetNoLimiter("loopback");

        return RateLimitPartition.GetFixedWindowLimiter(
            ip.ToString(),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PermitLimit,
                Window = Window,
                QueueLimit = 0,
            });
    }
}
