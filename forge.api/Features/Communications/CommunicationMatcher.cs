using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces.Communications;
using Forge.Core.Models.Communications;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Communications;

/// <summary>
/// Wave 8 — matches inbound communications (email or voice) against active
/// leads or customer contacts and writes Communication rows for each
/// hit. Single implementation regardless of provider — Gmail / IMAP /
/// Twilio / RingCentral / etc. all flow through here once their adapter
/// has translated to <see cref="InboundCommunication"/>.
///
/// Match precedence (per the design conversation):
/// <list type="number">
///   <item>Active <see cref="Lead"/> by Email (Email channel) or Phone
///   (Voice). Prefer the most-recently-active lead when multiple match
///   the same address.</item>
///   <item>Customer <see cref="Contact"/> by Email or Phone. Prefer the
///   most-recently-active contact (CreatedAt desc) when multiple match.</item>
///   <item>If still no match, return <c>Matched=false</c> with reason
///   "no-match" — the caller logs telemetry; a triage queue lands in a
///   later commit.</item>
/// </list>
///
/// Outbound communications (tenant → external) match against the To list
/// rather than the From; same precedence logic, just iterating the To
/// addresses one by one and creating one Communication per matched
/// recipient (so a salesperson CC'ing two leads on the same email
/// produces two interaction rows).
///
/// Idempotency: the first Communication row of a delivery carries the
/// provider's ExternalId with its Channel, so a re-delivered webhook or a
/// re-polled message with the same (Channel, ExternalId) is reported as
/// <see cref="CommunicationMatchResult.AlreadyIngested"/> and writes nothing.
/// Only that first row carries the id because the pair is unique.
/// </summary>
public class CommunicationMatcher(AppDbContext db) : ICommunicationMatcher
{
    public async Task<CommunicationMatchResult> MatchAndLogAsync(InboundCommunication comm, CancellationToken ct)
    {
        // For Inbound, the external-party address is `From`. For Outbound,
        // it's each entry in `To`. Build the candidate-address list once;
        // downstream lookup is the same shape.
        var candidates = comm.Direction == CommunicationDirection.Inbound
            ? new[] { Normalize(comm.From, comm.Kind) }.Where(s => !string.IsNullOrEmpty(s)).ToArray()
            : comm.To.Select(t => Normalize(t, comm.Kind)).Where(s => !string.IsNullOrEmpty(s)).ToArray();

        if (candidates.Length == 0)
        {
            return new CommunicationMatchResult(false, [], comm.ExternalId, "no-candidates");
        }

        var channel = ChannelFor(comm.Kind);
        if (!string.IsNullOrEmpty(comm.ExternalId))
        {
            var existingId = await db.Communications.AsNoTracking()
                .Where(c => c.Channel == channel && c.ExternalId == comm.ExternalId)
                .Select(c => (int?)c.Id)
                .FirstOrDefaultAsync(ct);
            if (existingId is int id)
                return new CommunicationMatchResult(true, [id], comm.ExternalId, null, AlreadyIngested: true);
        }

        var created = new List<Communication>();
        var matchCount = 0;

        foreach (var address in candidates)
        {
            var match = await ResolveMatchAsync(address, comm.Kind, ct);
            if (match is null) continue;

            matchCount++;
            var interaction = await LogInteractionAsync(comm, match, carriesExternalId: created.Count == 0, ct);
            if (interaction is not null) created.Add(interaction);
        }

        if (matchCount == 0)
        {
            return new CommunicationMatchResult(false, [], comm.ExternalId, "no-match");
        }

        // Lead-side matches write only an activity-log row (no Communication
        // yet — leads don't have a Contact entity), so a successful match may
        // produce zero entries in createdIds. Matched is true regardless.
        await db.SaveChangesAsync(ct);
        return new CommunicationMatchResult(true, created.Select(c => c.Id).ToList(), comm.ExternalId, null);
    }

    private static CommunicationChannel ChannelFor(CommunicationKind kind)
        => kind == CommunicationKind.Email ? CommunicationChannel.Email : CommunicationChannel.Voice;

    /// <summary>
    /// Normalize an email or phone for comparison. Email → lowercase + trim.
    /// Phone → strip everything but digits, drop leading 1 (US country
    /// code) for now (E.164 normalization is the next refinement; this
    /// covers ~90% of US-domestic matching). Returns empty when the input
    /// is unusable.
    /// </summary>
    internal static string Normalize(string? raw, CommunicationKind kind)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim();

        if (kind == CommunicationKind.Email)
        {
            return trimmed.ToLowerInvariant();
        }

        // Voice — keep only digits, then drop a leading 1 if the remainder
        // is 11 digits (US convention). Anything shorter than 7 digits is
        // garbage; return empty so the matcher skips it rather than
        // false-matching everyone with a 4-digit extension.
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits[0] == '1') digits = digits[1..];
        return digits.Length >= 7 ? digits : string.Empty;
    }

    private async Task<MatchTarget?> ResolveMatchAsync(string address, CommunicationKind kind, CancellationToken ct)
    {
        // 1. Active Lead first. Active = Status not in (Lost, Converted) so
        //    we don't reopen dead leads with a stray follow-up email.
        var leadId = kind == CommunicationKind.Email
            ? await db.Leads.AsNoTracking()
                .Where(l => l.Status != LeadStatus.Lost && l.Status != LeadStatus.Converted
                    && l.Email != null && l.Email.ToLower() == address)
                .OrderByDescending(l => l.UpdatedAt)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync(ct)
            : await db.Leads.AsNoTracking()
                .Where(l => l.Status != LeadStatus.Lost && l.Status != LeadStatus.Converted
                    && l.Phone != null)
                .ToListAsync(ct)
                .ContinueWith(t => t.Result
                    .Where(l => Normalize(l.Phone, CommunicationKind.Voice) == address)
                    .OrderByDescending(l => l.UpdatedAt)
                    .Select(l => (int?)l.Id)
                    .FirstOrDefault(), ct);

        if (leadId.HasValue) return new MatchTarget(leadId.Value, null, null);

        // 2. Customer Contact. Same shape — email exact-match (lowercased)
        //    or phone digits-match. Tiebreaker: most-recent CreatedAt.
        var contact = kind == CommunicationKind.Email
            ? await db.Contacts.AsNoTracking()
                .Include(c => c.Customer)
                .Where(c => c.Email != null && c.Email.ToLower() == address)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new { c.Id, c.CustomerId })
                .FirstOrDefaultAsync(ct)
            : (await db.Contacts.AsNoTracking()
                .Include(c => c.Customer)
                .Where(c => c.Phone != null)
                .ToListAsync(ct))
                .Where(c => Normalize(c.Phone, CommunicationKind.Voice) == address)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new { c.Id, c.CustomerId })
                .FirstOrDefault();

        if (contact is not null) return new MatchTarget(null, contact.CustomerId, contact.Id);

        return null;
    }

    private async Task<Communication?> LogInteractionAsync(
        InboundCommunication comm, MatchTarget target, bool carriesExternalId, CancellationToken ct)
    {
        // Communication requires a ContactId. For lead-side matches we
        // don't have a Contact yet (leads have a single inline contactName,
        // not a Contact entity) — so for now lead matches log only an
        // ActivityLog row anchored to the Lead and skip Communication.
        // The interaction-on-lead-without-contact path is a follow-on
        // (creating an inline "shadow" contact at conversion time, or
        // promoting LeadContact to a real entity).
        if (target.LeadId.HasValue)
        {
            var verb = comm.Direction == CommunicationDirection.Inbound
                ? "communication-received"
                : "communication-sent";
            var preview = TrimToPreview(comm.Subject ?? comm.Body ?? "(no content)");
            db.LogActivityAt(
                verb,
                $"{comm.Kind} {comm.Direction.ToString().ToLowerInvariant()} ({comm.ProviderId}): {preview}",
                ("Lead", target.LeadId.Value));

            // Phase 1j.1 — auto-progression. A `New` lead that gets its first
            // comm hit transitions to `Contacted` — the rep doesn't need to
            // remember to flip the chip. Only on the New→Contacted edge:
            // we never auto-advance past Contacted (that's a judgment call
            // the rep owns). Lost / Converted are filtered earlier in
            // ResolveMatchAsync, so they can't reach here.
            var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == target.LeadId.Value, ct);
            if (lead is not null && lead.Status == LeadStatus.New)
            {
                lead.Status = LeadStatus.Contacted;
                db.LogActivityAt(
                    "status-auto-advanced",
                    $"Status auto-advanced: New → Contacted (first communication received)",
                    ("Lead", target.LeadId.Value));
            }
            return null;
        }

        if (target.ContactId.HasValue && target.CustomerId.HasValue)
        {
            var interaction = new Communication
            {
                ContactId = target.ContactId.Value,
                PartyType = CommunicationPartyType.Contact,
                PartyId = target.ContactId.Value,
                MatchConfidence = CommunicationMatchConfidence.Exact,
                Channel = ChannelFor(comm.Kind),
                Flow = comm.Direction == CommunicationDirection.Inbound ? CommunicationFlow.Inbound : CommunicationFlow.Outbound,
                ExternalId = carriesExternalId && !string.IsNullOrEmpty(comm.ExternalId) ? comm.ExternalId : null,
                FromAddress = TrimTo(comm.From?.Trim(), 320),
                HandledByUserId = db.CurrentUserId,
                Type = comm.Kind == CommunicationKind.Email ? InteractionType.Email : InteractionType.Call,
                Subject = TrimToSubject(comm.Subject ?? comm.Body ?? "(auto-logged)"),
                Body = comm.Body,
                OccurredAt = comm.OccurredAt,
                DurationMinutes = comm.DurationMinutes,
            };
            db.Communications.Add(interaction);

            // Indexing-points pair on Customer + Contact, matching the
            // hand-logged interaction path's activity convention.
            db.LogActivityAt(
                comm.Direction == CommunicationDirection.Inbound ? "interaction-auto-received" : "interaction-auto-sent",
                $"Auto-logged {comm.Kind.ToString().ToLowerInvariant()} via {comm.ProviderId}: {TrimToPreview(interaction.Subject)}",
                ("Customer", target.CustomerId.Value),
                ("Contact", target.ContactId.Value));

            return interaction;
        }

        return null;
    }

    private static string? TrimTo(string? s, int max)
        => string.IsNullOrEmpty(s) ? null : s.Length <= max ? s : s[..max];

    private static string TrimToSubject(string s) => s.Length <= 200 ? s : s[..200];
    private static string TrimToPreview(string s) => s.Length <= 80 ? s : string.Concat(s.AsSpan(0, 77), "...");

    private sealed record MatchTarget(int? LeadId, int? CustomerId, int? ContactId);
}
