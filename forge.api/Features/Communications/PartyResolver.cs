using Microsoft.EntityFrameworkCore;

using Forge.Core.Constants;
using Forge.Core.Enums;
using Forge.Core.Interfaces.Communications;
using Forge.Data.Context;

namespace Forge.Api.Features.Communications;

/// <inheritdoc cref="IPartyResolver"/>
public class PartyResolver(AppDbContext db, ILogger<PartyResolver> logger) : IPartyResolver
{
    public async Task<PartyResolution> ResolveAsync(string address, CancellationToken ct)
    {
        var normalized = Normalize(address);
        if (string.IsNullOrEmpty(normalized))
            return PartyResolution.Unmatched("Sender address was empty or unparseable.");

        var domain = DomainOf(normalized);

        // ── Tier 1: the address itself is on file as a contact ──
        // Strongest signal available. A named person at a customer sent this
        // from the address we have for them.
        var contact = await db.Contacts
            .AsNoTracking()
            .Where(c => c.Email != null && c.Email.ToLower() == normalized)
            .Select(c => new { c.Id, c.CustomerId })
            .FirstOrDefaultAsync(ct);

        if (contact is not null)
        {
            return new PartyResolution(
                CommunicationMatchConfidence.Exact,
                CommunicationPartyType.Contact,
                contact.Id,
                contact.Id,
                $"Exact match on contact address {normalized}.");
        }

        // ── Tier 2: an explicit Address ingest rule ──
        // A shop can name a mailbox that is not a Contact — an orders@ alias,
        // or a buyer whose personal record nobody has created.
        var addressRule = await db.CommunicationIngestRules
            .AsNoTracking()
            .Where(r => r.IsEnabled
                && r.MatchType == IngestRuleMatchType.Address
                && r.Pattern == normalized)
            .FirstOrDefaultAsync(ct);

        if (addressRule is not null)
        {
            return addressRule.PartyId is int boundId
                ? new PartyResolution(
                    CommunicationMatchConfidence.Exact,
                    addressRule.PartyType ?? CommunicationPartyType.Customer,
                    boundId,
                    null,
                    $"Exact match on ingest rule for {normalized}.")
                : PartyResolution.Unmatched(
                    $"Address {normalized} is allowed for ingestion but bound to no party. "
                    + "Assign one in triage.");
        }

        if (string.IsNullOrEmpty(domain))
            return PartyResolution.Unmatched($"Could not read a domain from {normalized}.");

        // ── The hard block ──
        // Checked BEFORE any domain lookup, not after, so a domain rule that
        // should never have existed cannot match even if one somehow reached
        // the table — a direct INSERT, a restored backup, a row from before
        // this guard existed.
        if (FreeMailDomains.IsFreeMail(domain))
        {
            logger.LogInformation(
                "[PARTY-RESOLVE] {Address} is on a consumer mail domain; domain matching refused",
                normalized);

            return PartyResolution.Unmatched(
                $"{domain} is a consumer mail provider, so domain-wide matching does not apply. "
                + "Add this specific address as an ingest rule if it belongs to a customer.");
        }

        // ── Tier 3: a Domain ingest rule ──
        // Files the correspondence under the party, and stops there. Anyone at
        // the domain can send mail, so this can never authorize anything.
        var domainRule = await db.CommunicationIngestRules
            .AsNoTracking()
            .Where(r => r.IsEnabled
                && r.MatchType == IngestRuleMatchType.Domain
                && r.Pattern == domain)
            .FirstOrDefaultAsync(ct);

        if (domainRule?.PartyId is int domainPartyId)
        {
            return new PartyResolution(
                CommunicationMatchConfidence.Domain,
                domainRule.PartyType ?? CommunicationPartyType.Customer,
                domainPartyId,
                null,
                $"Domain match on {domain}. Files under the party; not sufficient to act on.");
        }

        if (domainRule is not null)
        {
            return PartyResolution.Unmatched(
                $"Domain {domain} is allowed for ingestion but bound to no party. Assign one in triage.");
        }

        // ── No rule ──
        // Deliberately terminal. Inferring a party from a shared domain with a
        // customer's contact would be a guess, and a guess here becomes
        // evidence that someone authorized an order.
        return PartyResolution.Unmatched(
            $"No contact or ingest rule matches {normalized}.");
    }

    /// <summary>Lowercase + trim. Mirrors <see cref="CommunicationMatcher"/>'s email normalization.</summary>
    internal static string Normalize(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim().ToLowerInvariant();

    /// <summary>Everything after the last '@'. Empty when there is no '@'.</summary>
    internal static string DomainOf(string normalizedAddress)
    {
        var at = normalizedAddress.LastIndexOf('@');
        return at < 0 || at == normalizedAddress.Length - 1
            ? string.Empty
            : normalizedAddress[(at + 1)..];
    }
}
