using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

using Forge.Api.Authorization;

namespace Forge.Api.Hubs;

/// <summary>
/// Push channel for the dark GL accounting suite. Carries a single server→client event,
/// <c>accountingChanged</c>, broadcast by
/// <see cref="Forge.Api.Features.Accounting.GlChangeBroadcastInterceptor"/> whenever accounting data is
/// written, so the accounting screens auto-refresh instead of relying on a manual Refresh button. No
/// client→server methods. A connection joins the <see cref="AccountingRoles.Group"/> group on connect only
/// when its user holds one of <see cref="AccountingRoles.Subscribers"/>, and the notification fans out to that
/// group alone. Any signed-in user may still connect, so a role the hub skips never sees a negotiate 403.
/// </summary>
[Authorize]
public sealed class AccountingHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (AccountingRoles.IsSubscriber(Context.User))
            await Groups.AddToGroupAsync(Context.ConnectionId, AccountingRoles.Group);

        await base.OnConnectedAsync();
    }
}
