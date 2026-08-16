using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces.Communications;
using Forge.Core.Models.Extraction;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Communications;

/// <summary>
/// Takes an inbound email and lands it: raw message and every attachment stored
/// and hashed, sender resolved to a party, candidate order extracted.
///
/// <para><b>This handler proposes and never commits.</b> It creates no sales
/// order. It produces a <see cref="Communication"/> with its evidence and, when
/// the conditions hold, an extraction good enough for a human to approve into a
/// draft. The automated prompt / manual response rule is structural here — there
/// is no code path from an inbound email to a live order without a person, and
/// no configuration switch that adds one.</para>
///
/// <para>Ingestion is opt-in: an address or domain with no enabled
/// <see cref="CommunicationIngestRule"/> and no matching contact is stored as
/// unmatched for triage rather than acted on. A shop mailbox holds personal
/// mail, and this must not vacuum it up.</para>
/// </summary>
public record IngestInboundEmailCommand(InboundEmail Email) : IRequest<IngestInboundEmailResult>;

/// <summary>
/// A parsed inbound message. Provider adapters produce this; the handler does
/// not care whether it came from IMAP, Gmail or a file drop.
/// </summary>
public record InboundEmail(
    /// <summary>Provider's stable id. The idempotency key — a re-poll or re-delivery must no-op.</summary>
    string ExternalId,
    string FromAddress,
    IReadOnlyList<string> ToAddresses,
    string? Subject,
    string? Body,
    DateTimeOffset SentAt,
    /// <summary>The raw RFC 5322 bytes, exactly as received. Stored verbatim — this is the evidence.</summary>
    byte[] RawMessage,
    IReadOnlyList<InboundEmailAttachment> Attachments,
    /// <summary>Derived from References / In-Reply-To, falling back to the root Message-ID.</summary>
    string? ThreadId = null);

public record InboundEmailAttachment(string Filename, string ContentType, byte[] Content);

public record IngestInboundEmailResult(
    int? CommunicationId,
    bool WasAlreadyIngested,
    CommunicationMatchConfidence Confidence,
    string Reason,
    ExtractionResult? Extraction,
    /// <summary>True when everything the draft path requires held. Still requires a human to approve.</summary>
    bool EligibleForDraft);

public class IngestInboundEmailHandler(
    AppDbContext db,
    IPartyResolver partyResolver,
    IArtifactStore artifacts,
    IOrderExtractor extractor,
    ILogger<IngestInboundEmailHandler> logger)
    : IRequestHandler<IngestInboundEmailCommand, IngestInboundEmailResult>
{
    public async Task<IngestInboundEmailResult> Handle(IngestInboundEmailCommand request, CancellationToken ct)
    {
        var email = request.Email;

        // Idempotency first, before any storage write. A re-delivered webhook or
        // a re-polled mailbox must not mint a second copy of the evidence.
        var existing = await db.Communications
            .AsNoTracking()
            .Where(c => c.Channel == CommunicationChannel.Email && c.ExternalId == email.ExternalId)
            .Select(c => new { c.Id, c.MatchConfidence })
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            return new IngestInboundEmailResult(
                existing.Id, WasAlreadyIngested: true,
                existing.MatchConfidence ?? CommunicationMatchConfidence.Unmatched,
                "Already ingested; nothing to do.", null, false);
        }

        var resolution = await partyResolver.ResolveAsync(email.FromAddress, CommunicationChannel.Email, ct);

        var communication = new Communication
        {
            Channel = CommunicationChannel.Email,
            Flow = CommunicationFlow.Inbound,
            Type = InteractionType.Email,
            ExternalId = email.ExternalId,
            ThreadId = email.ThreadId,
            FromAddress = PartyResolver.Normalize(email.FromAddress),
            Subject = Truncate(email.Subject, 200) ?? "(no subject)",
            Body = email.Body,
            OccurredAt = email.SentAt,
            PartyType = resolution.PartyType,
            PartyId = resolution.PartyId,
            ContactId = resolution.ContactId,
            MatchConfidence = resolution.Confidence,
            // No handler yet. An automatic import has no employee behind it, and
            // stamping one would put a name against work nobody did.
            HandledByUserId = null,
        };

        db.Communications.Add(communication);
        await db.SaveChangesAsync(ct);

        // Evidence before anything else reads it. The raw message is stored and
        // hashed first so the hash covers what arrived, not what survived
        // parsing.
        await using (var raw = new MemoryStream(email.RawMessage))
        {
            await artifacts.StoreAsync(
                communication.Id, CommunicationArtifactKind.Message, raw,
                "message/rfc822", $"{Sanitize(email.Subject) ?? "message"}.eml", ct);
        }

        var attachmentArtifacts = new List<CommunicationArtifact>();
        foreach (var attachment in email.Attachments)
        {
            ct.ThrowIfCancellationRequested();
            await using var stream = new MemoryStream(attachment.Content);
            attachmentArtifacts.Add(await artifacts.StoreAsync(
                communication.Id, CommunicationArtifactKind.Attachment, stream,
                attachment.ContentType, attachment.Filename, ct));
        }

        // File the correspondence against the party. Never against a bare Part —
        // the link table's CHECK refuses it, and this is where that matters.
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
            $"Inbound email from {communication.FromAddress} — {resolution.Reason}",
            ("Communication", communication.Id));

        await db.SaveChangesAsync(ct);

        // Extraction runs regardless of confidence: a triage reviewer benefits
        // from seeing what the message appears to say even when we cannot yet
        // say whose it is. It is the DRAFT path that requires Exact, not this.
        var extraction = await ExtractAsync(email, attachmentArtifacts, resolution, ct);

        var eligible = resolution.IsActionable && extraction.FoundAnything;

        logger.LogInformation(
            "[EMAIL-INGEST] {ExternalId} from {From}: {Confidence}, extraction {Found}, draft-eligible {Eligible}",
            email.ExternalId, communication.FromAddress, resolution.Confidence,
            extraction.FoundAnything ? "found candidates" : "empty", eligible);

        return new IngestInboundEmailResult(
            communication.Id, WasAlreadyIngested: false, resolution.Confidence,
            resolution.Reason, extraction, eligible);
    }

    /// <summary>
    /// Feed the extractor the message body plus any attachment we can read as
    /// text. Binary attachments (a PDF, until the extraction pipeline grows a
    /// text layer) are stored and hashed but contribute no text — which is a
    /// blank draft beside the original, exactly the intended degradation.
    /// </summary>
    private async Task<ExtractionResult> ExtractAsync(
        InboundEmail email,
        IReadOnlyList<CommunicationArtifact> attachmentArtifacts,
        PartyResolution resolution,
        CancellationToken ct)
    {
        var sources = new List<ExtractionSource>();

        if (!string.IsNullOrWhiteSpace(email.Body))
            sources.Add(new ExtractionSource(ExtractionSourceKind.MessageBody, email.Body));

        for (var i = 0; i < email.Attachments.Count; i++)
        {
            var attachment = email.Attachments[i];
            if (!LooksLikeText(attachment.ContentType)) continue;

            var text = System.Text.Encoding.UTF8.GetString(attachment.Content);
            sources.Add(new ExtractionSource(
                ExtractionSourceKind.Attachment, text,
                attachmentArtifacts.ElementAtOrDefault(i)?.Id, attachment.Filename));
        }

        var customerId = resolution.PartyType == CommunicationPartyType.Customer ? resolution.PartyId : null;

        try
        {
            return await extractor.ExtractAsync(
                new ExtractionRequest(sources, customerId, email.Subject), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failing extractor must never cost us the evidence. The message
            // and its attachments are already stored and hashed; the draft is
            // simply blank.
            logger.LogError(ex,
                "[EMAIL-INGEST] Extractor {ExtractorId} threw on {ExternalId}; degrading to a blank draft",
                extractor.ExtractorId, email.ExternalId);

            return ExtractionResult.Empty(extractor.ExtractorId,
                "The extractor failed on this message. The original is attached and can be read directly.");
        }
    }

    private static bool LooksLikeText(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType)
        && (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("csv", StringComparison.OrdinalIgnoreCase));

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value[..max]);

    /// <summary>Filename-safe form of the subject, for the stored .eml's display name only.</summary>
    private static string? Sanitize(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return null;
        var cleaned = new string(subject.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ').ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned[..Math.Min(cleaned.Length, 60)];
    }
}
