using System.Threading.RateLimiting;

namespace Forge.Api.Features.CustomerPortal;

/// <summary>
/// Per-IP limit on the anonymous portal sign-in endpoints (request-link,
/// exchange), chained after the global limiter. The per-contact resend
/// cooldown in <see cref="RequestMagicLinkHandler"/> is the primary control
/// against token rotation and email bombing; this is a second layer against
/// a single client spraying requests or guessing tokens. It is matched by
/// path rather than by endpoint metadata because UseRateLimiter runs before
/// UseRouting. The limit per minute comes from <see cref="PermitLimitKey"/>
/// (default <see cref="DefaultPermitLimit"/>). Unlike the global limiter it
/// has no loopback bypass, and requests with no client address share one
/// partition. It does not apply in Development. The client IP is the
/// forwarded one, since UseForwardedHeaders runs before UseRateLimiter.
/// </summary>
public static class PortalAuthRateLimit
{
    public const string PermitLimitKey = "Portal:AuthRateLimitPerMinute";

    public const int DefaultPermitLimit = 10;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private static readonly string[] LimitedPaths =
    [
        "/api/v1/portal/auth/request-link",
        "/api/v1/portal/auth/exchange",
    ];

    public static bool AppliesTo(PathString path)
        => LimitedPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase));

    public static int PermitLimit(IConfiguration config)
        => config.GetValue<int?>(PermitLimitKey) is int limit and > 0 ? limit : DefaultPermitLimit;

    public static PartitionedRateLimiter<HttpContext> Create(bool isDevelopment)
        => PartitionedRateLimiter.Create<HttpContext, string>(ctx => Partition(ctx, isDevelopment));

    public static RateLimitPartition<string> Partition(HttpContext ctx, bool isDevelopment)
    {
        if (isDevelopment || !AppliesTo(ctx.Request.Path))
            return RateLimitPartition.GetNoLimiter("unlimited");

        var permitLimit = PermitLimit(ctx.RequestServices.GetRequiredService<IConfiguration>());
        return RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = Window,
                QueueLimit = 0,
            });
    }
}
