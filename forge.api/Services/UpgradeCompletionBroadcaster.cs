using Microsoft.AspNetCore.SignalR;

using Forge.Api.Hubs;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Services;

/// <summary>
/// Tells every console the upgrade is over.
/// <para>
/// The start broadcast is sent by the API that dispatched the job — then that container is
/// destroyed, so nothing on the old side can announce completion. This runs on the new API's first
/// boot: if the agent's most recent job ended in the window an upgrade could plausibly have been
/// running, it broadcasts terminal state. That releases the lock and triggers the one hard reload
/// each console owes, its SPA bundle having been replaced underneath it.
/// </para>
/// <para>
/// The recency window is what keeps an ordinary API restart from telling a shop floor full of
/// tablets to reload for an upgrade that happened last week.
/// </para>
/// </summary>
public class UpgradeCompletionBroadcaster(
    IServiceScopeFactory scopes,
    IHubContext<NotificationHub> hub,
    IClock clock,
    ILogger<UpgradeCompletionBroadcaster> log) : BackgroundService
{
    private static readonly TimeSpan RecentWindow = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var agent = scope.ServiceProvider.GetRequiredService<IDeployAgentClient>();
        if (!agent.IsConfigured) return;

        // Enough delay to not race SignalR's own startup. Consoles that reconnect before this
        // fires are covered by the marker file, so this is a convenience, not a guarantee.
        await Task.Delay(TimeSpan.FromSeconds(5), ct);

        var last = await agent.GetLastJobAsync(ct);
        if (last is null || last.State == "running") return;
        if (last.EndedAt is not { } endedAt || clock.UtcNow - endedAt > RecentWindow) return;

        log.LogInformation("Announcing completion of deploy job {JobId} ({State})", last.Id, last.State);
        await hub.Clients.All.SendAsync(
            "upgradeStateChanged",
            new UpgradeStatusModel(last.State == "succeeded" ? "succeeded" : "stopped", last.StartedAt, endedAt, null, null),
            ct);
    }
}
