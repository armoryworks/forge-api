using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces.Communications;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Communications;

/// <summary>
/// Lands a completed call the same way an inbound email lands: party resolved,
/// recording and transcript stored as hashed artifacts, nothing committed.
///
/// <para>Channel-agnostic by design. As the source discussion put it — it does
/// not matter that it is email and it does not matter that it is voice, it is a
/// communication. This handler is deliberately near-identical to
/// <see cref="IngestInboundEmailHandler"/>, and the difference is confined to
/// what a call has instead of a body: a duration, a recording, a transcript.</para>
///
/// <para><b>Recording is not assumed.</b> Two-party-consent states make recording
/// a legal decision rather than a feature toggle, so a call with no recording is
/// the normal case and is ingested exactly the same. The transcript, when there
/// is one, is stored as its own artifact — it is a derived document and hashing
/// it separately from the audio is what lets a dispute distinguish "the recording
/// says X" from "our transcription of it says X".</para>
/// </summary>
public record IngestVoiceCallCommand(InboundCall Call) : IRequest<IngestVoiceCallResult>;

/// <summary>
/// A completed call, normalised. Adapters — Twilio, Asterisk via forge-voice,
/// RingCentral — translate their native payload into this.
/// </summary>
public record InboundCall(
    /// <summary>Provider's call id (Twilio CallSid, Asterisk channel id). The idempotency key.</summary>
    string ExternalId,
    /// <summary>The external party's number. For an inbound call this is the caller.</summary>
    string FromNumber,
    string? ToNumber,
    CommunicationFlow Flow,
    DateTimeOffset OccurredAt,
    int? DurationMinutes,
    /// <summary>Provider's own status string, kept verbatim for triage.</summary>
    string? Disposition = null,
    /// <summary>Transcript text, when the platform produced one.</summary>
    string? Transcript = null,
    /// <summary>Recording audio. Null when the install does not record, which is the common case.</summary>
    byte[]? Recording = null,
    string? RecordingContentType = null);

public record IngestVoiceCallResult(
    int? CommunicationId,
    bool WasAlreadyIngested,
    CommunicationMatchConfidence Confidence,
    string Reason);

public class IngestVoiceCallHandler(
    AppDbContext db,
    IPartyResolver partyResolver,
    IArtifactStore artifacts,
    ILogger<IngestVoiceCallHandler> logger)
    : IRequestHandler<IngestVoiceCallCommand, IngestVoiceCallResult>
{
    public async Task<IngestVoiceCallResult> Handle(IngestVoiceCallCommand request, CancellationToken ct)
    {
        var call = request.Call;

        var existing = await db.Communications
            .AsNoTracking()
            .Where(c => c.Channel == CommunicationChannel.Voice && c.ExternalId == call.ExternalId)
            .Select(c => new { c.Id, c.MatchConfidence })
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            return new IngestVoiceCallResult(
                existing.Id, true,
                existing.MatchConfidence ?? CommunicationMatchConfidence.Unmatched,
                "Already ingested; nothing to do.");
        }

        // The external party is the caller on an inbound call and the callee on
        // an outbound one — the number that identifies who we spoke to.
        var externalNumber = call.Flow == CommunicationFlow.Inbound
            ? call.FromNumber
            : call.ToNumber ?? call.FromNumber;

        var resolution = await partyResolver.ResolveAsync(
            externalNumber, CommunicationChannel.Voice, ct);

        var communication = new Communication
        {
            Channel = CommunicationChannel.Voice,
            Flow = call.Flow,
            Type = InteractionType.Call,
            ExternalId = call.ExternalId,
            FromAddress = PartyResolver.Normalize(externalNumber, CommunicationChannel.Voice),
            Subject = BuildSubject(call, resolution),
            Body = call.Transcript,
            OccurredAt = call.OccurredAt,
            DurationMinutes = call.DurationMinutes,
            PartyType = resolution.PartyType,
            PartyId = resolution.PartyId,
            ContactId = resolution.ContactId,
            MatchConfidence = resolution.Confidence,
            // Set when someone answers and tags the call, not at ingestion.
            HandledByUserId = null,
        };

        db.Communications.Add(communication);
        await db.SaveChangesAsync(ct);

        if (call.Recording is { Length: > 0 })
        {
            await using var audio = new MemoryStream(call.Recording);
            await artifacts.StoreAsync(
                communication.Id, CommunicationArtifactKind.Attachment, audio,
                call.RecordingContentType ?? "audio/wav",
                $"call-{call.ExternalId}.wav", ct);
        }

        if (!string.IsNullOrWhiteSpace(call.Transcript))
        {
            // Hashed separately from the audio on purpose. A transcript is a
            // derived document, and a dispute needs to be able to tell "the
            // recording says X" apart from "our transcription says X".
            await using var text = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(call.Transcript));
            await artifacts.StoreAsync(
                communication.Id, CommunicationArtifactKind.Attachment, text,
                "text/plain", $"call-{call.ExternalId}-transcript.txt", ct);
        }

        if (resolution.PartyType is CommunicationPartyType.Customer or CommunicationPartyType.Contact
            && resolution.PartyId is int partyId)
        {
            var customerId = resolution.PartyType == CommunicationPartyType.Contact
                ? await db.Contacts.Where(c => c.Id == partyId).Select(c => (int?)c.CustomerId).FirstOrDefaultAsync(ct)
                : partyId;

            if (customerId is int cid)
            {
                db.CommunicationLinks.Add(new CommunicationLink
                {
                    CommunicationId = communication.Id,
                    EntityType = CommunicationLink.Types.Customer,
                    EntityId = cid,
                    PartyType = resolution.PartyType,
                    PartyId = resolution.PartyId,
                });
            }
        }

        db.LogActivityAt(
            "communication-ingested",
            $"{call.Flow} call with {communication.FromAddress} — {resolution.Reason}",
            ("Communication", communication.Id));

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "[VOICE-INGEST] {ExternalId} {Flow} {Number}: {Confidence}, {Duration}min, recording {HasRecording}",
            call.ExternalId, call.Flow, communication.FromAddress, resolution.Confidence,
            call.DurationMinutes, call.Recording is { Length: > 0 });

        return new IngestVoiceCallResult(
            communication.Id, false, resolution.Confidence, resolution.Reason);
    }

    private static string BuildSubject(InboundCall call, PartyResolution resolution)
    {
        var direction = call.Flow == CommunicationFlow.Inbound ? "Inbound" : "Outbound";
        var who = resolution.PartyId is null ? call.FromNumber : resolution.Reason;
        var duration = call.DurationMinutes is int m and > 0 ? $" ({m} min)" : string.Empty;
        var subject = $"{direction} call{duration}";

        // Keep the unmatched number in the subject — it is the only handle a
        // triage reviewer has before the party is assigned.
        if (resolution.PartyId is null)
            subject += $" from {call.FromNumber}";

        _ = who;
        return subject.Length <= 200 ? subject : subject[..200];
    }
}
