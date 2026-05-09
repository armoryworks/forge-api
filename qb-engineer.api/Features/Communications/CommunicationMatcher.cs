using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Entities;
using QBEngineer.Core.Enums;
using QBEngineer.Core.Interfaces.Communications;
using QBEngineer.Core.Models.Communications;
using QBEngineer.Data.Context;
using QBEngineer.Data.Extensions;

namespace QBEngineer.Api.Features.Communications;

/// <summary>
/// Wave 8 — matches inbound communications (email or voice) against active
/// leads or customer contacts and writes ContactInteraction rows for each
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
/// addresses one by one and creating one ContactInteraction per matched
/// recipient (so a salesperson CC'ing two leads on the same email
/// produces two interaction rows).
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

        var createdIds = new List<int>();
        var matchCount = 0;

        foreach (var address in candidates)
        {
            var match = await ResolveMatchAsync(address, comm.Kind, ct);
            if (match is null) continue;

            matchCount++;
            var interactionId = await LogInteractionAsync(comm, address, match, ct);
            if (interactionId.HasValue) createdIds.Add(interactionId.Value);
        }

        if (matchCount == 0)
        {
            return new CommunicationMatchResult(false, [], comm.ExternalId, "no-match");
        }

        // Lead-side matches write only an activity-log row (no ContactInteraction
        // yet — leads don't have a Contact entity), so a successful match may
        // produce zero entries in createdIds. Matched is true regardless.
        await db.SaveChangesAsync(ct);
        return new CommunicationMatchResult(true, createdIds, comm.ExternalId, null);
    }

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

    private async Task<int?> LogInteractionAsync(InboundCommunication comm, string address, MatchTarget target, CancellationToken ct)
    {
        // ContactInteraction requires a ContactId. For lead-side matches we
        // don't have a Contact yet (leads have a single inline contactName,
        // not a Contact entity) — so for now lead matches log only an
        // ActivityLog row anchored to the Lead and skip ContactInteraction.
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
            // Auto-logged interactions belong to "system" — no human user
            // recorded them. Use the AppDbContext.CurrentUserId if a request
            // is in flight (webhook-receiver path); fall back to a
            // sentinel "system" user id zero which the controller display
            // logic shows as "(automated)".
            var userId = db.CurrentUserId ?? 0;
            var interaction = new ContactInteraction
            {
                ContactId = target.ContactId.Value,
                UserId = userId,
                Type = comm.Kind == CommunicationKind.Email ? InteractionType.Email : InteractionType.Call,
                Subject = TrimToSubject(comm.Subject ?? comm.Body ?? "(auto-logged)"),
                Body = comm.Body,
                InteractionDate = comm.OccurredAt,
                DurationMinutes = comm.DurationMinutes,
            };
            db.ContactInteractions.Add(interaction);

            // Indexing-points pair on Customer + Contact, matching the
            // hand-logged interaction path's activity convention.
            db.LogActivityAt(
                comm.Direction == CommunicationDirection.Inbound ? "interaction-auto-received" : "interaction-auto-sent",
                $"Auto-logged {comm.Kind.ToString().ToLowerInvariant()} via {comm.ProviderId}: {TrimToPreview(interaction.Subject)}",
                ("Customer", target.CustomerId.Value),
                ("Contact", target.ContactId.Value));

            // Return the not-yet-persisted entity's ref so the caller can
            // capture the id after SaveChanges. We use a placeholder of -1
            // here and rely on the matcher's outer SaveChanges flushing all
            // pending interactions in one go; the caller then re-reads the
            // id off the entity. (Simpler: just return 0 and let the caller
            // not depend on the id for now — it's telemetry.)
            return 0;
        }

        return null;
    }

    private static string TrimToSubject(string s) => s.Length <= 200 ? s : s[..200];
    private static string TrimToPreview(string s) => s.Length <= 80 ? s : string.Concat(s.AsSpan(0, 77), "...");

    private sealed record MatchTarget(int? LeadId, int? CustomerId, int? ContactId);
}
