using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

using Forge.Api.Authorization;

namespace Forge.Api.Hubs;

[Authorize]
public class BoardHub : Hub
{
    [Authorize(Roles = JobRoles.BoardRead)]
    public async Task JoinBoard(int trackTypeId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"board:{trackTypeId}");
    }

    public async Task LeaveBoard(int trackTypeId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"board:{trackTypeId}");
    }

    [Authorize(Roles = JobRoles.Read)]
    public async Task JoinJob(int jobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"job:{jobId}");
    }

    public async Task LeaveJob(int jobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"job:{jobId}");
    }
}
