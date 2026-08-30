using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Admin;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Upgrading this install from inside Forge. The controller decides who may upgrade and audits the
/// decision; forge-agent, a privileged process on the host, executes it by running the same gated
/// deploy CLI an operator would. The API never touches docker — it is one of the containers an
/// upgrade replaces.
/// <para>
/// Bootstrap-exempt for the same reason as the database recovery surface: upgrading is one of the
/// ways an install in a bad state gets fixed, so it must never be the thing a broken capability
/// snapshot gates off. Access is the Admin role, and the agent must be separately installed and
/// wired — a shop that wants upgrades to remain its integrator's job simply does not install it,
/// which removes the mechanism rather than merely hiding the button.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/admin/updates")]
[Authorize(Roles = "Admin")]
[CapabilityBootstrap]
public class AdminUpdatesController(IMediator mediator, IDeployAgentClient agent) : ControllerBase
{
    /// <summary>Running versus recorded version per tier, and any upgrade already in flight.</summary>
    [HttpGet("state")]
    public async Task<ActionResult<DeployStateModel>> GetState()
        => Ok(await mediator.Send(new GetDeployStateQuery()));

    /// <summary>Whether a newer release is published for every tier this box runs.</summary>
    [HttpGet("available")]
    public async Task<ActionResult<DeployAvailabilityModel>> GetAvailable()
        => Ok(await mediator.Send(new GetDeployAvailabilityQuery()));

    /// <summary>
    /// Starts an upgrade, rollback or per-tier deploy. Audits the request and locks every console
    /// before dispatching, while this API is still alive to do both.
    /// </summary>
    [HttpPost("jobs")]
    public async Task<ActionResult<DeployJobModel>> StartJob([FromBody] DeployJobRequest request)
    {
        if (!agent.IsConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, AgentMissing);

        var result = await mediator.Send(new StartDeployJobCommand(
            request.Action,
            request.Service,
            request.Tag,
            request.Confirm,
            request.ApprovedFromJobId,
            CurrentUserId()));

        return result.Status switch
        {
            "started" => Accepted(result.Job),
            "busy" => Conflict(new { error = result.Error }),
            "unavailable" => StatusCode(StatusCodes.Status503ServiceUnavailable, AgentMissing),
            _ => BadRequest(new { error = result.Error }),
        };
    }

    /// <summary>One job's state, including the destructive statements awaiting disposition.</summary>
    [HttpGet("jobs/{jobId}")]
    public async Task<ActionResult<DeployJobModel>> GetJob(string jobId)
    {
        var job = await mediator.Send(new GetDeployJobQuery(jobId));
        return job is null ? NotFound() : Ok(job);
    }

    /// <summary>Deploy log from a byte offset, for incremental append.</summary>
    [HttpGet("jobs/{jobId}/log")]
    public async Task<ActionResult<string>> GetJobLog(string jobId, [FromQuery] long offset = 0)
        => Ok(await mediator.Send(new GetDeployJobLogQuery(jobId, offset)));

    private static object AgentMissing => new
    {
        error = "No upgrade agent is installed on this box. Upgrade from the server with ./forge-upgrade.sh.",
    };

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

/// <param name="Action">update, updateApprove, rollback or deployService.</param>
/// <param name="Confirm">Must be APPLY for updateApprove — the destructive-schema gate.</param>
/// <param name="ApprovedFromJobId">The halted job whose statements the operator accepted.</param>
public record DeployJobRequest(
    string Action,
    string? Service,
    string? Tag,
    string? Confirm,
    string? ApprovedFromJobId);
