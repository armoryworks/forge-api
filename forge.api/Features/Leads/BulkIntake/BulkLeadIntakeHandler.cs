using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Leads.BulkIntake;

/// <summary>
/// Phase 1r / Batch 4 — bulk lead intake pipeline. Same handler powers
/// both preview (dry-run) and commit modes; the only difference is
/// whether passing rows are inserted + activity-logged at the end.
///
/// Pipeline per row:
///   1. Required-field gate (per-strategy: cold-call needs phone,
///      cold-email needs email, list-purchase needs companyName +
///      one identifier, etc.)
///   2. Within-batch dedup — if an earlier row already claimed this
///      email or phone, mark this one DuplicateWithinBatch and skip.
///   3. Suppression check — if the row's email matches a Lead or
///      Contact whose preferences carry an active EmailOptOut for
///      email-channel strategies (or CallOptOut for phone-channel),
///      mark SuppressedOptOut and skip. CooldownUntil &gt; now is
///      InCooldown regardless of channel.
///   4. Existing-Lead dedup — match by email OR phone (case-
///      insensitive on email). DuplicateExistingLead.
///   5. Existing-Contact dedup — match by email OR phone against any
///      Contact row. DuplicateExistingContact (already a customer).
///   6. If all gates pass, mark Created.
///
/// Commit mode then inserts the Created rows and emits one
/// rolled-up activity-log entry per lead ("bulk-intake-created").
///
/// Caller carries an opaque ExternalRowKey per row so the UI can
/// match preview results back to its source rows.
/// </summary>
public record BulkLeadIntakeCommand(BulkLeadIntakeRequest Request, bool Commit) : IRequest<BulkLeadIntakeResponseModel>;

public class BulkLeadIntakeHandler(AppDbContext db, IClock clock)
    : IRequestHandler<BulkLeadIntakeCommand, BulkLeadIntakeResponseModel>
{
    public async Task<BulkLeadIntakeResponseModel> Handle(BulkLeadIntakeCommand request, CancellationToken ct)
    {
        var rows = request.Request.Rows ?? [];
        if (rows.Count == 0)
            return new BulkLeadIntakeResponseModel(0, 0, 0, []);
        if (rows.Count > 1000)
            throw new InvalidOperationException("Bulk intake is capped at 1000 rows per upload.");

        var strategy = request.Request.Strategy;
        var now = clock.UtcNow;

        // Pre-load matching candidates in a single query each — much
        // faster than per-row lookups for 100+ row uploads.
        var emails = rows.Where(r => !string.IsNullOrWhiteSpace(r.Email))
            .Select(r => r.Email!.Trim().ToLowerInvariant()).Distinct().ToList();
        var phones = rows.Where(r => !string.IsNullOrWhiteSpace(r.Phone))
            .Select(r => DigitsOnly(r.Phone!)).Where(p => p.Length > 0).Distinct().ToList();

        var existingLeadsByEmail = await db.Leads.AsNoTracking()
            .Where(l => l.Email != null && emails.Contains(l.Email.ToLower()))
            .ToDictionaryAsync(l => l.Email!.ToLowerInvariant(), l => l.Id, ct);

        var existingLeadsByPhone = await db.Leads.AsNoTracking()
            .Where(l => l.Phone != null)
            .Select(l => new { l.Id, l.Phone })
            .ToListAsync(ct);
        var existingLeadsByPhoneMap = existingLeadsByPhone
            .Where(l => l.Phone != null)
            .GroupBy(l => DigitsOnly(l.Phone!))
            .Where(g => g.Key.Length > 0 && phones.Contains(g.Key))
            .ToDictionary(g => g.Key, g => g.First().Id);

        var existingContactsByEmail = await db.Contacts.AsNoTracking()
            .Where(c => c.Email != null && emails.Contains(c.Email.ToLower()))
            .ToDictionaryAsync(c => c.Email!.ToLowerInvariant(), c => c.Id, ct);

        var existingContactsByPhone = await db.Contacts.AsNoTracking()
            .Where(c => c.Phone != null)
            .Select(c => new { c.Id, c.Phone })
            .ToListAsync(ct);
        var existingContactsByPhoneMap = existingContactsByPhone
            .Where(c => c.Phone != null)
            .GroupBy(c => DigitsOnly(c.Phone!))
            .Where(g => g.Key.Length > 0 && phones.Contains(g.Key))
            .ToDictionary(g => g.Key, g => g.First().Id);

        // Suppression — pull every preferences row whose owner could
        // collide. Cooldown-until is a row-level field; channel opt-outs
        // are per-channel booleans the strategy resolves at decision time.
        var leadIdsToCheck = existingLeadsByEmail.Values.Concat(existingLeadsByPhoneMap.Values).Distinct().ToList();
        var contactIdsToCheck = existingContactsByEmail.Values.Concat(existingContactsByPhoneMap.Values).Distinct().ToList();

        var leadPrefs = await db.LeadOutreachPreferences.AsNoTracking()
            .Where(p => leadIdsToCheck.Contains(p.LeadId))
            .ToDictionaryAsync(p => p.LeadId, p => p, ct);
        var contactPrefs = await db.ContactOutreachPreferences.AsNoTracking()
            .Where(p => contactIdsToCheck.Contains(p.ContactId))
            .ToDictionaryAsync(p => p.ContactId, p => p, ct);

        // Per-row processing
        var results = new List<BulkLeadIntakeRowResult>(rows.Count);
        var seenEmails = new HashSet<string>();
        var seenPhones = new HashSet<string>();
        var leadsToCreate = new List<Lead>();

        foreach (var row in rows)
        {
            var key = row.ExternalRowKey;

            // 0. Sanity — a lead needs a company OR a contact name (individuals
            // have no company).
            if (string.IsNullOrWhiteSpace(row.CompanyName) && string.IsNullOrWhiteSpace(row.ContactName))
            {
                results.Add(new BulkLeadIntakeRowResult(key, BulkLeadIntakeRowStatus.Invalid, null, null, null,
                    "companyName or contactName is required"));
                continue;
            }

            // 1. Per-strategy required-field gate
            var missing = CheckRequiredFields(strategy, row);
            if (missing is not null)
            {
                results.Add(new BulkLeadIntakeRowResult(key, BulkLeadIntakeRowStatus.MissingRequiredField, null, null, null,
                    $"{strategy} requires {missing}"));
                continue;
            }

            var emailNorm = row.Email?.Trim().ToLowerInvariant();
            var phoneNorm = !string.IsNullOrWhiteSpace(row.Phone) ? DigitsOnly(row.Phone) : null;

            // 2. Within-batch dedup
            if (emailNorm is not null && seenEmails.Contains(emailNorm))
            {
                results.Add(new BulkLeadIntakeRowResult(key, BulkLeadIntakeRowStatus.DuplicateWithinBatch, null, null, null,
                    "Duplicate email earlier in batch"));
                continue;
            }
            if (phoneNorm is not null && phoneNorm.Length > 0 && seenPhones.Contains(phoneNorm))
            {
                results.Add(new BulkLeadIntakeRowResult(key, BulkLeadIntakeRowStatus.DuplicateWithinBatch, null, null, null,
                    "Duplicate phone earlier in batch"));
                continue;
            }

            // 3. Suppression — match against Lead first, then Contact.
            //    Email-channel strategies check EmailOptOut; phone-channel
            //    check CallOptOut; mixed strategies check both. Cooldown
            //    is channel-agnostic.
            var suppression = CheckSuppression(strategy, emailNorm, phoneNorm,
                existingLeadsByEmail, existingLeadsByPhoneMap, leadPrefs,
                existingContactsByEmail, existingContactsByPhoneMap, contactPrefs, now);
            if (suppression is not null)
            {
                results.Add(suppression with { ExternalRowKey = key });
                continue;
            }

            // 4. Existing-Lead dedup
            if (emailNorm is not null && existingLeadsByEmail.TryGetValue(emailNorm, out var existingLeadId))
            {
                results.Add(new BulkLeadIntakeRowResult(key, BulkLeadIntakeRowStatus.DuplicateExistingLead,
                    null, existingLeadId, "Lead", "Email matches existing lead"));
                continue;
            }
            if (phoneNorm is not null && phoneNorm.Length > 0 && existingLeadsByPhoneMap.TryGetValue(phoneNorm, out var existingLeadIdByPhone))
            {
                results.Add(new BulkLeadIntakeRowResult(key, BulkLeadIntakeRowStatus.DuplicateExistingLead,
                    null, existingLeadIdByPhone, "Lead", "Phone matches existing lead"));
                continue;
            }

            // 5. Existing-Contact dedup (already a customer)
            if (emailNorm is not null && existingContactsByEmail.TryGetValue(emailNorm, out var existingContactId))
            {
                results.Add(new BulkLeadIntakeRowResult(key, BulkLeadIntakeRowStatus.DuplicateExistingContact,
                    null, existingContactId, "Contact", "Email matches existing customer contact"));
                continue;
            }
            if (phoneNorm is not null && phoneNorm.Length > 0 && existingContactsByPhoneMap.TryGetValue(phoneNorm, out var existingContactIdByPhone))
            {
                results.Add(new BulkLeadIntakeRowResult(key, BulkLeadIntakeRowStatus.DuplicateExistingContact,
                    null, existingContactIdByPhone, "Contact", "Phone matches existing customer contact"));
                continue;
            }

            // 6. Clean — record claim against this batch's seen sets
            if (emailNorm is not null) seenEmails.Add(emailNorm);
            if (phoneNorm is not null && phoneNorm.Length > 0) seenPhones.Add(phoneNorm);

            var lead = new Lead
            {
                CompanyName = row.CompanyName?.Trim() ?? string.Empty,
                ContactName = row.ContactName?.Trim(),
                Email = string.IsNullOrWhiteSpace(row.Email) ? null : row.Email.Trim(),
                Phone = string.IsNullOrWhiteSpace(row.Phone) ? null : row.Phone.Trim(),
                Source = !string.IsNullOrWhiteSpace(row.Source)
                    ? row.Source.Trim()
                    : DefaultSourceForStrategy(strategy, request.Request.CampaignTag),
                Notes = row.Notes?.Trim(),
                Status = LeadStatus.New,
                // Phase 1r / Batch 5 — Campaign FK + initial OutreachState
                // (Queued — workers pull from the queue; until then the
                // lead waits in this state).
                CampaignId = request.Request.CampaignId,
                OutreachState = OutreachState.Queued,
            };
            leadsToCreate.Add(lead);

            // Result row gets the Lead.Id post-commit; for preview mode
            // we report Created without an id (frontend doesn't need it
            // until a real commit fires).
            results.Add(new BulkLeadIntakeRowResult(key, BulkLeadIntakeRowStatus.Created,
                null, null, null, null));
        }

        if (request.Commit && leadsToCreate.Count > 0)
        {
            db.Leads.AddRange(leadsToCreate);
            await db.SaveChangesAsync(ct);

            // Re-stamp Created results with the persisted ids. Order is
            // preserved across the result list and the leadsToCreate list
            // since we appended both in lock-step.
            var createdEnumerator = leadsToCreate.GetEnumerator();
            for (var i = 0; i < results.Count; i++)
            {
                if (results[i].Status == BulkLeadIntakeRowStatus.Created && createdEnumerator.MoveNext())
                {
                    results[i] = results[i] with { CreatedLeadId = createdEnumerator.Current.Id };
                }
            }

            // Activity log: one rolled-up entry on each lead summarizing
            // the bulk-intake provenance per the activity-log rules.
            var summary = $"Created via bulk intake — strategy: {strategy}" +
                (string.IsNullOrWhiteSpace(request.Request.CampaignTag) ? "" : $", batch: {request.Request.CampaignTag}");
            foreach (var lead in leadsToCreate)
            {
                db.LogActivityAt("bulk-intake-created", summary, ("Lead", lead.Id));
            }
            await db.SaveChangesAsync(ct);
        }

        var createdCount = results.Count(r => r.Status == BulkLeadIntakeRowStatus.Created);
        var skippedCount = results.Count - createdCount;
        return new BulkLeadIntakeResponseModel(rows.Count, createdCount, skippedCount, results);
    }

    private static string? CheckRequiredFields(BulkLeadIntakeStrategy strategy, BulkLeadIntakeRow row)
    {
        var hasEmail = !string.IsNullOrWhiteSpace(row.Email);
        var hasPhone = !string.IsNullOrWhiteSpace(row.Phone);
        return strategy switch
        {
            BulkLeadIntakeStrategy.ColdCall => hasPhone ? null : "phone",
            BulkLeadIntakeStrategy.ColdEmail => hasEmail ? null : "email",
            BulkLeadIntakeStrategy.WebinarAttendee => hasEmail ? null : "email",
            BulkLeadIntakeStrategy.TradeShowFollowup => (hasEmail || hasPhone) ? null : "email or phone",
            BulkLeadIntakeStrategy.ListPurchase => (hasEmail || hasPhone) ? null : "email or phone",
            BulkLeadIntakeStrategy.ManualEntry => null,
            _ => null,
        };
    }

    private static BulkLeadIntakeRowResult? CheckSuppression(
        BulkLeadIntakeStrategy strategy,
        string? emailNorm, string? phoneNorm,
        Dictionary<string, int> leadsByEmail, Dictionary<string, int> leadsByPhone,
        Dictionary<int, LeadOutreachPreferences> leadPrefs,
        Dictionary<string, int> contactsByEmail, Dictionary<string, int> contactsByPhone,
        Dictionary<int, ContactOutreachPreferences> contactPrefs,
        DateTimeOffset now)
    {
        var checksEmail = strategy is BulkLeadIntakeStrategy.ColdEmail or BulkLeadIntakeStrategy.WebinarAttendee
            or BulkLeadIntakeStrategy.TradeShowFollowup or BulkLeadIntakeStrategy.ListPurchase;
        var checksPhone = strategy is BulkLeadIntakeStrategy.ColdCall
            or BulkLeadIntakeStrategy.TradeShowFollowup or BulkLeadIntakeStrategy.ListPurchase;

        // Lead side
        if (emailNorm is not null && leadsByEmail.TryGetValue(emailNorm, out var leadId)
            && leadPrefs.TryGetValue(leadId, out var lprefs))
        {
            if (lprefs.CooldownUntil > now)
                return new BulkLeadIntakeRowResult("", BulkLeadIntakeRowStatus.InCooldown, null, leadId, "Lead",
                    $"Lead is in cooldown until {lprefs.CooldownUntil:yyyy-MM-dd}");
            if (checksEmail && lprefs.EmailOptOut)
                return new BulkLeadIntakeRowResult("", BulkLeadIntakeRowStatus.SuppressedOptOut, null, leadId, "Lead",
                    "Lead has opted out of email");
            if (checksPhone && lprefs.CallOptOut)
                return new BulkLeadIntakeRowResult("", BulkLeadIntakeRowStatus.SuppressedOptOut, null, leadId, "Lead",
                    "Lead has opted out of calls");
        }
        if (phoneNorm is not null && phoneNorm.Length > 0 && leadsByPhone.TryGetValue(phoneNorm, out var leadIdByPhone)
            && leadPrefs.TryGetValue(leadIdByPhone, out var lprefsPhone))
        {
            if (lprefsPhone.CooldownUntil > now)
                return new BulkLeadIntakeRowResult("", BulkLeadIntakeRowStatus.InCooldown, null, leadIdByPhone, "Lead",
                    $"Lead is in cooldown until {lprefsPhone.CooldownUntil:yyyy-MM-dd}");
            if (checksPhone && lprefsPhone.CallOptOut)
                return new BulkLeadIntakeRowResult("", BulkLeadIntakeRowStatus.SuppressedOptOut, null, leadIdByPhone, "Lead",
                    "Lead has opted out of calls");
            if (checksEmail && lprefsPhone.EmailOptOut)
                return new BulkLeadIntakeRowResult("", BulkLeadIntakeRowStatus.SuppressedOptOut, null, leadIdByPhone, "Lead",
                    "Lead has opted out of email");
        }

        // Contact side (post-conversion suppression)
        if (emailNorm is not null && contactsByEmail.TryGetValue(emailNorm, out var contactId)
            && contactPrefs.TryGetValue(contactId, out var cprefs))
        {
            if (cprefs.CooldownUntil > now)
                return new BulkLeadIntakeRowResult("", BulkLeadIntakeRowStatus.InCooldown, null, contactId, "Contact",
                    $"Contact is in cooldown until {cprefs.CooldownUntil:yyyy-MM-dd}");
            if (checksEmail && cprefs.EmailOptOut)
                return new BulkLeadIntakeRowResult("", BulkLeadIntakeRowStatus.SuppressedOptOut, null, contactId, "Contact",
                    "Contact has opted out of email");
            if (checksPhone && cprefs.CallOptOut)
                return new BulkLeadIntakeRowResult("", BulkLeadIntakeRowStatus.SuppressedOptOut, null, contactId, "Contact",
                    "Contact has opted out of calls");
        }
        if (phoneNorm is not null && phoneNorm.Length > 0 && contactsByPhone.TryGetValue(phoneNorm, out var contactIdByPhone)
            && contactPrefs.TryGetValue(contactIdByPhone, out var cprefsPhone))
        {
            if (cprefsPhone.CooldownUntil > now)
                return new BulkLeadIntakeRowResult("", BulkLeadIntakeRowStatus.InCooldown, null, contactIdByPhone, "Contact",
                    $"Contact is in cooldown until {cprefsPhone.CooldownUntil:yyyy-MM-dd}");
            if (checksPhone && cprefsPhone.CallOptOut)
                return new BulkLeadIntakeRowResult("", BulkLeadIntakeRowStatus.SuppressedOptOut, null, contactIdByPhone, "Contact",
                    "Contact has opted out of calls");
        }

        return null;
    }

    private static string DefaultSourceForStrategy(BulkLeadIntakeStrategy strategy, string? campaignTag)
    {
        var basis = strategy switch
        {
            BulkLeadIntakeStrategy.ColdCall => "Cold Call",
            BulkLeadIntakeStrategy.ColdEmail => "Cold Email",
            BulkLeadIntakeStrategy.TradeShowFollowup => "Trade Show",
            BulkLeadIntakeStrategy.WebinarAttendee => "Webinar",
            BulkLeadIntakeStrategy.ListPurchase => "Purchased List",
            BulkLeadIntakeStrategy.ManualEntry => "Bulk Import",
            _ => "Bulk Import",
        };
        return string.IsNullOrWhiteSpace(campaignTag) ? basis : $"{basis} — {campaignTag}";
    }

    private static string DigitsOnly(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        var i = 0;
        foreach (var c in s) if (char.IsDigit(c)) buf[i++] = c;
        return new string(buf[..i]);
    }
}
