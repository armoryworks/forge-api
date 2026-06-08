using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Forge.Api.Hubs;

/// <summary>
/// Push channel for the dark GL accounting suite. Carries a single server→client event,
/// <c>accountingChanged</c>, broadcast by
/// <see cref="Forge.Api.Features.Accounting.GlChangeBroadcastInterceptor"/> whenever accounting data is
/// written, so the accounting screens auto-refresh instead of relying on a manual Refresh button. No
/// client→server methods and no groups today — a single install is one logical ledger, so the notification
/// fans out to every connected client.
/// </summary>
[Authorize]
public sealed class AccountingHub : Hub
{
}
