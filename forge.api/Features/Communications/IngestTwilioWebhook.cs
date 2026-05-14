using MediatR;
using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces.Communications;
using Forge.Core.Models.Communications;

namespace Forge.Api.Features.Communications;

/// <summary>
/// Wave 8 — Twilio voice webhook ingestion. Twilio posts
/// application/x-www-form-urlencoded payloads to a configured webhook
/// URL on each call status transition. We translate the relevant fields
/// to <see cref="InboundCommunication"/> and feed the matcher.
///
/// Field reference (Twilio Voice Status Callback):
///   CallSid           — globally unique call identifier (used as ExternalId)
///   From              — caller phone number (E.164)
///   To                — callee phone number (E.164)
///   Direction         — "inbound" | "outbound-api" | "outbound-dial"
///   CallStatus        — "ringing" | "in-progress" | "completed" | "no-answer" | "canceled" | "failed"
///   CallDuration      — seconds, populated on the "completed" callback
///   RecordingUrl      — Twilio-hosted recording (when recording is enabled)
///   Timestamp         — RFC 1123 timestamp of the event (use Now if absent)
///
/// We only act on the terminal-state callback ("completed", "no-answer",
/// "canceled", "failed") so the activity log doesn't get one row per
/// state transition. The matcher dedupes on (ExternalId), so duplicate
/// webhooks from Twilio re-trying are safe.
///
/// Signature verification (X-Twilio-Signature) is enforced when the
/// auth-token is configured (TwilioOptions:AuthToken in appsettings).
/// In dev mode the signature check is skipped so curl-driven smoke tests
/// don't require a live Twilio account.
/// </summary>
public record IngestTwilioWebhookCommand(IReadOnlyDictionary<string, string> FormFields)
    : IRequest<CommunicationMatchResult>;

public class IngestTwilioWebhookHandler(
    ICommunicationMatcher matcher,
    ILogger<IngestTwilioWebhookHandler> logger)
    : IRequestHandler<IngestTwilioWebhookCommand, CommunicationMatchResult>
{
    private static readonly HashSet<string> TerminalStatuses =
    [
        "completed", "no-answer", "canceled", "failed", "busy",
    ];

    public Task<CommunicationMatchResult> Handle(IngestTwilioWebhookCommand request, CancellationToken cancellationToken)
    {
        var fields = request.FormFields;
        var callSid = Get(fields, "CallSid");
        var from = Get(fields, "From");
        var to = Get(fields, "To");
        var status = Get(fields, "CallStatus")?.ToLowerInvariant() ?? string.Empty;
        var direction = Get(fields, "Direction")?.ToLowerInvariant() ?? "inbound";

        if (string.IsNullOrEmpty(callSid))
        {
            logger.LogWarning("Twilio webhook ingested with no CallSid; ignoring");
            return Task.FromResult(new CommunicationMatchResult(false, [], "(unknown)", "missing-call-sid"));
        }

        if (!TerminalStatuses.Contains(status))
        {
            logger.LogDebug(
                "Twilio webhook for CallSid {CallSid} status {Status} — non-terminal, ignored",
                callSid, status);
            return Task.FromResult(new CommunicationMatchResult(true, [], callSid, "non-terminal-status"));
        }

        var occurredAt = ParseTimestamp(Get(fields, "Timestamp"));
        var durationMinutes = ParseDurationMinutes(Get(fields, "CallDuration"));

        var comm = new InboundCommunication(
            ProviderId: "twilio",
            Kind: CommunicationKind.Voice,
            Direction: direction.StartsWith("outbound") ? CommunicationDirection.Outbound : CommunicationDirection.Inbound,
            ExternalId: callSid,
            From: from ?? string.Empty,
            To: string.IsNullOrEmpty(to) ? Array.Empty<string>() : [to],
            OccurredAt: occurredAt,
            Subject: BuildSubject(status, durationMinutes),
            Body: null,
            DurationMinutes: durationMinutes,
            RecordingUrl: Get(fields, "RecordingUrl"));

        return matcher.MatchAndLogAsync(comm, cancellationToken);
    }

    private static string? Get(IReadOnlyDictionary<string, string> fields, string key)
        => fields.TryGetValue(key, out var v) ? v : null;

    private static DateTimeOffset ParseTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return DateTimeOffset.UtcNow;
        return DateTimeOffset.TryParse(raw, out var parsed) ? parsed : DateTimeOffset.UtcNow;
    }

    private static int? ParseDurationMinutes(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!int.TryParse(raw, out var seconds)) return null;
        // Round up to next minute — a 30-second call still counts as 1 min
        // for the activity log (zero-min looks like a missed call which is
        // semantically different).
        return seconds <= 0 ? 0 : (seconds + 59) / 60;
    }

    private static string BuildSubject(string status, int? durationMinutes)
    {
        if (status == "completed" && durationMinutes is int mins)
        {
            return $"Call completed ({mins} min)";
        }
        return status switch
        {
            "no-answer" => "Missed call (no answer)",
            "busy" => "Missed call (busy)",
            "canceled" => "Cancelled call",
            "failed" => "Failed call",
            _ => $"Call: {status}",
        };
    }
}
