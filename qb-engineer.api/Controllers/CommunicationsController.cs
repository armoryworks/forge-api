using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using QBEngineer.Api.Capabilities;
using QBEngineer.Api.Features.Communications;
using QBEngineer.Core.Enums;
using QBEngineer.Core.Models.Communications;

namespace QBEngineer.Api.Controllers;

/// <summary>
/// Wave 8 — communication-sync admin surface. Three concerns:
/// (1) per-user mailbox / phone connection CRUD ("My Connections" panel),
/// (2) generic ingest endpoint that drives the matcher from any source
///     (webhook bridge, admin test, n8n / Zapier translation, etc.),
/// (3) provider-agnostic auth handshakes (deferred to phase 1b/c).
///
/// Capability gating is per-endpoint rather than per-controller because
/// email and voice use distinct caps (CAP-EXT-EMAIL-SYNC vs
/// CAP-EXT-VOIP-SYNC) and the read endpoints are kind-agnostic. The
/// per-kind enforcement on Create/Ingest happens at the handler tier
/// via <see cref="ICapabilitySnapshotProvider"/>.
/// </summary>
[ApiController]
[Route("api/v1/communications")]
[Authorize]
public class CommunicationsController(IMediator mediator, ICapabilitySnapshotProvider snapshots) : ControllerBase
{
    /// <summary>
    /// List the calling user's connections. Kind-agnostic — surfaces both
    /// email and voice rows. The read path is intentionally NOT capability-
    /// gated so a user can still see (and remove) a stale connection after
    /// an admin disables the underlying capability.
    /// </summary>
    [HttpGet("connections")]
    public async Task<ActionResult<List<CommunicationSyncConfigResponseModel>>> GetConnections(CancellationToken ct)
        => Ok(await mediator.Send(new GetCommunicationSyncConfigsQuery(), ct));

    /// <summary>
    /// Create a new connection. The relevant capability (email or voice)
    /// must be enabled — checked here at the boundary so the 403 carries
    /// the same envelope as middleware-gated endpoints.
    /// </summary>
    [HttpPost("connections")]
    public async Task<ActionResult<CommunicationSyncConfigResponseModel>> CreateConnection(
        CreateCommunicationSyncConfigRequestModel request, CancellationToken ct)
    {
        EnsureKindEnabled(request.Kind);

        var result = await mediator.Send(new CreateCommunicationSyncConfigCommand(
            request.Kind, request.ProviderId, request.DisplayLabel,
            request.ExternalAccountId, request.ConfigJson), ct);
        return CreatedAtAction(nameof(GetConnections), null, result);
    }

    /// <summary>
    /// Soft-delete a connection. No capability check — the user must always
    /// be able to disconnect (otherwise disabling the cap would strand
    /// the connection forever).
    /// </summary>
    [HttpDelete("connections/{id:int}")]
    public async Task<IActionResult> DeleteConnection(int id, CancellationToken ct)
    {
        await mediator.Send(new DeleteCommunicationSyncConfigCommand(id), ct);
        return NoContent();
    }

    /// <summary>
    /// Trigger a one-shot sync for the user's connection. The Hangfire
    /// recurring job (every 15 min) handles the unattended path; this
    /// endpoint is the "Sync now" affordance for impatient users right
    /// after they connect a mailbox / phone.
    /// </summary>
    [HttpPost("connections/{id:int}/sync")]
    public async Task<ActionResult<SyncCommunicationConnectionResult>> SyncConnection(int id, CancellationToken ct)
    {
        var result = await mediator.Send(new SyncCommunicationConnectionCommand(id), ct);
        return Ok(result);
    }

    /// <summary>
    /// Generic ingest endpoint — drives the matcher from a JSON payload.
    /// Same shape provider adapters translate to before calling
    /// <see cref="QBEngineer.Core.Interfaces.Communications.ICommunicationMatcher"/>.
    /// Restricted to admins because (a) misuse can pollute activity logs,
    /// (b) provider-specific webhook receivers are the supported path for
    /// production traffic.
    /// </summary>
    [HttpPost("ingest")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CommunicationMatchResult>> Ingest(
        InboundCommunication communication, CancellationToken ct)
    {
        EnsureKindEnabled(communication.Kind);
        var result = await mediator.Send(new IngestCommunicationCommand(communication), ct);
        return Ok(result);
    }

    private void EnsureKindEnabled(CommunicationKind kind)
    {
        var capability = kind == CommunicationKind.Email ? "CAP-EXT-EMAIL-SYNC" : "CAP-EXT-VOIP-SYNC";
        if (!snapshots.Current.IsEnabled(capability))
        {
            // Throw the same exception type the MediatR gate-behavior throws so
            // the global ExceptionHandlingMiddleware emits the standard 403
            // capability-disabled envelope (X-Capability-Disabled header etc).
            throw new CapabilityDisabledException(capability);
        }
    }
}
