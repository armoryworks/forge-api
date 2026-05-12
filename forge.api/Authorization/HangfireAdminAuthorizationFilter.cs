using Hangfire.Dashboard;

namespace Forge.Api.Authorization;

/// <summary>
/// Requires an authenticated user in the "Admin" role to access the
/// Hangfire dashboard. Without this filter, Hangfire's default is
/// local-requests-only, which silently exposes the dashboard if a
/// reverse proxy ever proxies /hangfire.
/// </summary>
public sealed class HangfireAdminAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true
            && httpContext.User.IsInRole("Admin");
    }
}
