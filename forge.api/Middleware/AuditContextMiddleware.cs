using System.Security.Claims;

using Forge.Data.Context;

namespace Forge.Api.Middleware;

/// <summary>
/// Sets the CurrentUserId, CurrentIpAddress, and CurrentUserAgent on AppDbContext
/// for automatic activity-log + system-wide audit-log writes.
/// Must run after UseAuthentication/UseAuthorization.
/// </summary>
public class AuditContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        var userIdClaim = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(userIdClaim, out var userId))
        {
            db.CurrentUserId = userId;
        }

        db.CurrentIpAddress = context.Connection.RemoteIpAddress?.ToString();

        var userAgent = context.Request.Headers.UserAgent.ToString();
        if (!string.IsNullOrEmpty(userAgent))
        {
            // audit_log_entries.user_agent is varchar(500); truncate defensively
            db.CurrentUserAgent = userAgent.Length > 500 ? userAgent[..500] : userAgent;
        }

        await next(context);
    }
}
