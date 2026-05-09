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
    /// IMAP-specific connect endpoint. Test-authenticates against the live
    /// server before persisting, encrypts the password server-side, builds
    /// the canonical ConfigJson shape. Distinct from the generic
    /// <see cref="CreateConnection"/> path so client mistakes can't poison
    /// the JSON or land a broken row.
    /// </summary>
    [HttpPost("connections/imap")]
    public async Task<ActionResult<CommunicationSyncConfigResponseModel>> ConnectImap(
        ConnectImapCommand request, CancellationToken ct)
    {
        EnsureKindEnabled(CommunicationKind.Email);
        var result = await mediator.Send(request, ct);
        return CreatedAtAction(nameof(GetConnections), null, result);
    }

    /// <summary>
    /// Phase 1k.2 — OAuth-IMAP authorize-flow initiation. Returns the
    /// authorize URL the SPA opens (popup or new tab). The provider
    /// redirects back to the SPA's callback page, which posts the
    /// (code, state) pair to <see cref="CompleteOAuthImap"/>.
    /// </summary>
    [HttpPost("oauth/imap/{provider}/begin")]
    public async Task<ActionResult<BeginOAuthImapResult>> BeginOAuthImap(
        string provider, CancellationToken ct)
    {
        EnsureKindEnabled(CommunicationKind.Email);
        var result = await mediator.Send(new BeginOAuthImapCommand(provider), ct);
        return Ok(result);
    }

    /// <summary>
    /// Phase 1k.2 — OAuth-IMAP authorize-flow completion. Exchanges the
    /// authorization code for access + refresh tokens, persists the
    /// connection. Authenticated user must match the state token's owner
    /// (CSRF guard inside the handler).
    /// </summary>
    [HttpPost("oauth/imap/{provider}/complete")]
    public async Task<ActionResult<CommunicationSyncConfigResponseModel>> CompleteOAuthImap(
        string provider, [FromBody] CompleteOAuthImapBody body, CancellationToken ct)
    {
        EnsureKindEnabled(CommunicationKind.Email);
        var result = await mediator.Send(
            new CompleteOAuthImapCommand(provider, body.Code, body.State), ct);
        return CreatedAtAction(nameof(GetConnections), null, result);
    }

    public sealed record CompleteOAuthImapBody(string Code, string State);

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

    /// <summary>
    /// Twilio voice webhook endpoint — receives form-urlencoded status
    /// callbacks. Configured at the Twilio side as the Voice Status
    /// Callback URL on the relevant phone number(s).
    ///
    /// Authentication is per-request via the X-Twilio-Signature HMAC
    /// header, NOT JWT — Twilio doesn't carry a user identity. Therefore
    /// AllowAnonymous + signature verification handles auth.
    /// </summary>
    [HttpPost("webhook/twilio")]
    [AllowAnonymous]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<ActionResult<CommunicationMatchResult>> TwilioWebhook(
        [FromForm] IFormCollection form,
        [FromServices] ITwilioSignatureVerifier verifier,
        [FromServices] Microsoft.Extensions.Logging.ILogger<CommunicationsController> log,
        CancellationToken ct)
    {
        if (!snapshots.Current.IsEnabled("CAP-EXT-VOIP-SYNC"))
        {
            throw new CapabilityDisabledException("CAP-EXT-VOIP-SYNC");
        }

        var fields = form.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());
        var fullUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
        var signature = Request.Headers["X-Twilio-Signature"].ToString();

        if (verifier.IsConfigured && !verifier.Verify(fullUrl, fields, signature))
        {
            log.LogWarning(
                "Twilio webhook signature verification FAILED for CallSid={CallSid}",
                fields.GetValueOrDefault("CallSid", "?"));
            return Unauthorized(new { error = "twilio-signature-mismatch" });
        }

        var result = await mediator.Send(new IngestTwilioWebhookCommand(fields), ct);
        // Always return 200 to Twilio — non-2xx triggers their retry loop
        // which would re-deliver the same call status, even when the
        // matcher legitimately found no match (cold-pitch number).
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
