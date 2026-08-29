using System.Text.Json;

using MediatR;
using Microsoft.AspNetCore.SignalR;

using Forge.Api.Hubs;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Admin;

/// <param name="ApprovedFromJobId">The halted job whose destructive statements the operator
/// accepted. Recorded in the audit entry so the decision is attributable to a person and to the
/// exact DDL they were shown.</param>
public record StartDeployJobCommand(
    string Action,
    string? Service,
    string? Tag,
    string? Confirm,
    string? ApprovedFromJobId,
    int ActorUserId) : IRequest<DeployJobStartResultModel>;

/// <summary>
/// Dispatches an upgrade to the agent, after auditing it and locking every console.
/// <para>
/// Order matters: the audit row and the broadcast both happen <b>before</b> the job starts, while
/// this API is still alive to write and send them. Seconds later this container is destroyed.
/// </para>
/// </summary>
public class StartDeployJobHandler(
    IDeployAgentClient agent,
    ISystemAuditWriter audit,
    IHubContext<NotificationHub> hub,
    IClock clock) : IRequestHandler<StartDeployJobCommand, DeployJobStartResultModel>
{
    public async Task<DeployJobStartResultModel> Handle(StartDeployJobCommand request, CancellationToken ct)
    {
        var approved = request.ApprovedFromJobId is { Length: > 0 } priorId
            ? (await agent.GetJobAsync(priorId, ct))?.NeedsApproval
            : null;

        await audit.WriteAsync(
            action: request.Action == "updateApprove" ? "UpgradeDestructiveApproved" : "UpgradeStarted",
            userId: request.ActorUserId,
            details: JsonSerializer.Serialize(new
            {
                request.Action,
                request.Service,
                request.Tag,
                ApprovedStatements = approved?.Statements.Select(s => s.Statement).ToList(),
            }),
            ct: ct);

        var result = await agent.StartJobAsync(request.Action, request.Service, request.Tag, request.Confirm, ct);
        if (result.Status == "started")
            await BroadcastAsync("running", clock.UtcNow, null, ct);

        return result;
    }

    // Clients.All: the payload is therefore a security boundary, not copy. It carries no tag, tier
    // name, job id or schema statement — those would reach every logged-in tablet in the shop.
    // Operator detail stays on the authenticated admin/updates path.
    private Task BroadcastAsync(string state, DateTimeOffset startedAt, DateTimeOffset? endedAt, CancellationToken ct) =>
        hub.Clients.All.SendAsync(
            "upgradeStateChanged",
            new UpgradeStatusModel(
                state,
                startedAt,
                endedAt,
                null,
                state == "running" ? "Forge is being updated. This screen will come back on its own." : null),
            ct);
}
