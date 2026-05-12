using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Forge.Api.Hubs;

[Authorize]
public class BoardHub : Hub
{
    public async Task JoinBoard(int trackTypeId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"board:{trackTypeId}");
    }

    public async Task LeaveBoard(int trackTypeId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"board:{trackTypeId}");
    }

    public async Task JoinJob(int jobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"job:{jobId}");
    }

    public async Task LeaveJob(int jobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"job:{jobId}");
    }
}
