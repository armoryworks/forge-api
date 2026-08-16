using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Communications;
using Forge.Core.Enums;

namespace Forge.Api.Controllers;

/// <summary>
/// Call-event ingestion from a self-hosted voice bridge.
///
/// <para><b>This endpoint was missing.</b> The forge-voice service has always
/// posted call-state changes to <c>forge-api/api/v1/voice/webhook</c> — its
/// header comment says so — but nothing here answered that URL, so every event
/// it emitted went nowhere. That is why the bridge has never logged a call.</para>
///
/// <para>Twilio has its own webhook (<c>IngestTwilioWebhook</c>) because its
/// payload shape and signature scheme are entirely different. Both converge on
/// the same <see cref="IngestVoiceCallCommand"/>, so a call logged through
/// Asterisk and one logged through Twilio are the same kind of record.</para>
/// </summary>
[ApiController]
[Route("api/v1/voice")]
[RequiresCapability("CAP-EXT-VOIP-SYNC")]
public class VoiceController(
    IMediator mediator,
    IConfiguration configuration,
    ILogger<VoiceController> logger) : ControllerBase
{
    /// <summary>
    /// Call-state callback from forge-voice.
    ///
    /// <para>Authenticated by a shared secret rather than a user token: the
    /// bridge is a service on the same host with no user context. The secret is
    /// compared in fixed time, and a request without it is refused outright
    /// rather than being processed and logged, because an unauthenticated caller
    /// could otherwise fabricate call records — which in this system are
    /// evidence.</para>
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(
        [FromBody] VoiceWebhookPayload payload,
        [FromHeader(Name = "X-Forge-Voice-Secret")] string? secret,
        CancellationToken ct)
    {
        var expected = configuration["Voice:WebhookSecret"];

        if (string.IsNullOrWhiteSpace(expected))
        {
            // Refusing beats defaulting open. An install that has not configured
            // the secret has not opted into accepting call events.
            logger.LogWarning("[VOICE-WEBHOOK] Rejected: Voice:WebhookSecret is not configured on this install.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                "Voice webhook is not configured on this install.");
        }

        if (!FixedTimeEquals(secret, expected))
        {
            logger.LogWarning("[VOICE-WEBHOOK] Rejected a call event with a bad or missing shared secret.");
            return Unauthorized();
        }

        // Only terminal states become records. Ringing and Up are transient UI
        // signals; writing one row per transition would turn the customer
        // timeline into a state-machine log.
        if (!IsTerminal(payload.State))
        {
            logger.LogDebug("[VOICE-WEBHOOK] Ignoring non-terminal state {State} for {CallId}",
                payload.State, payload.CallId);
            return Ok(new { ignored = true, reason = "non-terminal state" });
        }

        if (string.IsNullOrWhiteSpace(payload.CallId) || string.IsNullOrWhiteSpace(payload.From))
            return BadRequest("callId and from are required.");

        var result = await mediator.Send(new IngestVoiceCallCommand(new InboundCall(
            ExternalId: payload.CallId,
            FromNumber: payload.From,
            ToNumber: payload.To,
            Flow: string.Equals(payload.Direction, "outbound", StringComparison.OrdinalIgnoreCase)
                ? CommunicationFlow.Outbound
                : CommunicationFlow.Inbound,
            OccurredAt: payload.OccurredAt ?? DateTimeOffset.UtcNow,
            DurationMinutes: payload.DurationSeconds is int s and > 0
                ? (int)Math.Ceiling(s / 60.0)
                : null,
            Disposition: payload.State,
            Transcript: payload.Transcript)), ct);

        return Ok(new
        {
            communicationId = result.CommunicationId,
            alreadyIngested = result.WasAlreadyIngested,
            confidence = result.Confidence.ToString(),
            reason = result.Reason,
        });
    }

    private static bool IsTerminal(string? state) =>
        state is not null && state.ToLowerInvariant() is
            "hangup" or "completed" or "noanswer" or "no-answer" or "busy" or "failed" or "canceled" or "cancelled";

    /// <summary>
    /// Constant-time comparison. A shared secret compared with == leaks its
    /// prefix through timing, which is a real attack on an endpoint that mints
    /// evidence.
    /// </summary>
    private static bool FixedTimeEquals(string? provided, string expected)
    {
        if (string.IsNullOrEmpty(provided)) return false;

        var a = System.Text.Encoding.UTF8.GetBytes(provided);
        var b = System.Text.Encoding.UTF8.GetBytes(expected);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }
}

/// <summary>
/// The shape forge-voice posts. Matches the bridge's <c>postWebhook</c> payload
/// plus the fields it will carry once recording and transcription land.
/// </summary>
public record VoiceWebhookPayload
{
    /// <summary>Asterisk channel id. Stable for the life of the call.</summary>
    public string? CallId { get; init; }

    /// <summary>Ringing | Up | Hangup | Busy | NoAnswer | Failed. Only terminal values are recorded.</summary>
    public string? State { get; init; }

    public string? From { get; init; }
    public string? To { get; init; }

    /// <summary>"inbound" or "outbound". Defaults to inbound when absent.</summary>
    public string? Direction { get; init; }

    public DateTimeOffset? OccurredAt { get; init; }
    public int? DurationSeconds { get; init; }
    public string? Transcript { get; init; }
}
