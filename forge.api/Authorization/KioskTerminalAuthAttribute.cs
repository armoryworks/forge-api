using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Authorization;

/// <summary>
/// Gate for shop-floor kiosk endpoints that cannot carry a user JWT.
/// The kiosk device is issued a deviceToken during admin-led setup
/// (persisted as a KioskTerminal row); all subsequent kiosk traffic
/// must carry that token in the X-Kiosk-Device-Token header.
/// Falls through (200) when the caller is already authenticated with
/// a normal JWT, so admin users browsing via the web app still work.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class KioskTerminalAuthAttribute : Attribute, IAsyncAuthorizationFilter
{
    public const string HeaderName = "X-Kiosk-Device-Token";

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated == true)
            return;

        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var headerValues))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var token = headerValues.ToString().Trim();
        if (string.IsNullOrEmpty(token))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var terminal = await db.KioskTerminals
            .AsNoTracking()
            .Where(t => t.DeviceToken == token && t.IsActive)
            .Select(t => new { t.Id, t.TeamId })
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (terminal is null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        context.HttpContext.Items["KioskTerminalId"] = terminal.Id;
        context.HttpContext.Items["KioskTeamId"] = terminal.TeamId;
    }
}
