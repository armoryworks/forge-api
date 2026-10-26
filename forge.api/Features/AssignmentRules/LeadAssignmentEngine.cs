using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.AssignmentRules;

/// <summary>
/// Applies the active assignment rules to new, unowned leads. Rules run in priority order (then name);
/// the first rule that matches a lead and yields an active rep sets <see cref="Lead.AssignedToUserId"/>.
/// Round-robin rotation advances the rule's <see cref="AssignmentRule.LastAssignedUserId"/> cursor on
/// the tracked rule, so the caller's save persists it with the leads. Returns the rule that assigned
/// each lead, for the caller's activity log.
/// </summary>
public static class LeadAssignmentEngine
{
    public static async Task<Dictionary<Lead, AssignmentRule>> AssignAsync(
        AppDbContext db, IReadOnlyCollection<Lead> leads, CancellationToken ct)
    {
        var assigned = new Dictionary<Lead, AssignmentRule>();
        var unowned = leads.Where(l => l.AssignedToUserId is null).ToList();
        if (unowned.Count == 0)
            return assigned;

        var rules = await db.AssignmentRules
            .Where(r => r.IsActive && r.DeletedAt == null)
            .OrderBy(r => r.Priority).ThenBy(r => r.Name).ThenBy(r => r.Id)
            .ToListAsync(ct);
        if (rules.Count == 0)
            return assigned;

        var specs = rules.ToDictionary(r => r.Id, r => RuleSpec.Parse(r.Spec));
        var activeUserIds = (await db.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .Select(u => u.Id)
            .ToListAsync(ct)).ToHashSet();

        var accountIds = unowned.Where(l => l.AccountId.HasValue).Select(l => l.AccountId!.Value).Distinct().ToList();
        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        Dictionary<string, int>? ownerByCompany = null;
        if (rules.Any(r => r.Kind == AssignmentRuleKind.AccountBased))
        {
            var companies = unowned
                .Select(l => l.CompanyName.Trim().ToLowerInvariant())
                .Where(c => c.Length > 0)
                .Distinct()
                .ToList();
            ownerByCompany = (await db.Leads.AsNoTracking()
                    .Where(l => l.AssignedToUserId != null && companies.Contains(l.CompanyName.Trim().ToLower()))
                    .OrderByDescending(l => l.CreatedAt)
                    .Select(l => new { Company = l.CompanyName.Trim().ToLower(), Owner = l.AssignedToUserId!.Value })
                    .ToListAsync(ct))
                .Where(x => activeUserIds.Contains(x.Owner))
                .GroupBy(x => x.Company)
                .ToDictionary(g => g.Key, g => g.First().Owner);
        }

        foreach (var lead in unowned)
        {
            var account = lead.AccountId is int accountId ? accounts.GetValueOrDefault(accountId) : null;
            foreach (var rule in rules)
            {
                var owner = rule.Kind == AssignmentRuleKind.AccountBased
                    ? StickyOwner(lead, account, activeUserIds, ownerByCompany)
                    : Matches(rule.Kind, specs[rule.Id], account) ? NextRep(rule, specs[rule.Id], activeUserIds) : null;
                if (owner is null)
                    continue;

                lead.AssignedToUserId = owner;
                assigned[lead] = rule;
                var company = lead.CompanyName.Trim().ToLowerInvariant();
                if (ownerByCompany is not null && company.Length > 0)
                    ownerByCompany.TryAdd(company, owner.Value);
                break;
            }
        }

        return assigned;
    }

    public static async Task LogAssignmentsAsync(
        AppDbContext db, IReadOnlyDictionary<Lead, AssignmentRule> assigned, CancellationToken ct)
    {
        if (assigned.Count == 0)
            return;

        var ownerIds = assigned.Keys.Select(l => l.AssignedToUserId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => ownerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", ct);

        foreach (var (lead, rule) in assigned)
        {
            var owner = names.GetValueOrDefault(lead.AssignedToUserId!.Value, $"user {lead.AssignedToUserId}");
            db.LogActivityAt("assigned", $"Assigned to {owner} by rule '{rule.Name}'", ("Lead", lead.Id));
        }
    }

    private static bool Matches(AssignmentRuleKind kind, RuleSpec spec, Account? account) => kind switch
    {
        AssignmentRuleKind.RoundRobin => true,
        AssignmentRuleKind.Territory => account is not null
            && ((account.State is { } state && spec.States.Contains(state.Trim(), StringComparer.OrdinalIgnoreCase))
                || (account.PostalCode is { } zip && spec.ZipPrefixes.Any(p => p.Length > 0 && zip.Trim().StartsWith(p, StringComparison.OrdinalIgnoreCase)))),
        AssignmentRuleKind.Industry => account?.Industry is { } industry
            && spec.Industries.Contains(industry.Trim(), StringComparer.OrdinalIgnoreCase),
        _ => false,
    };

    private static int? NextRep(AssignmentRule rule, RuleSpec spec, HashSet<int> activeUserIds)
    {
        var reps = spec.RepIds.Where(activeUserIds.Contains).Distinct().ToList();
        if (reps.Count == 0)
            return null;

        var last = rule.LastAssignedUserId is int id ? reps.IndexOf(id) : -1;
        var next = reps[(last + 1) % reps.Count];
        rule.LastAssignedUserId = next;
        return next;
    }

    private static int? StickyOwner(Lead lead, Account? account, HashSet<int> activeUserIds, Dictionary<string, int>? ownerByCompany)
    {
        if (account?.OwnerUserId is int accountOwner && activeUserIds.Contains(accountOwner))
            return accountOwner;

        var company = lead.CompanyName.Trim().ToLowerInvariant();
        return company.Length > 0 && ownerByCompany is not null && ownerByCompany.TryGetValue(company, out var owner)
            ? owner
            : null;
    }

    private sealed record RuleSpec(List<int> RepIds, List<string> States, List<string> ZipPrefixes, List<string> Industries)
    {
        private static readonly RuleSpec Empty = new([], [], [], []);

        public static RuleSpec Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Empty;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return Empty;
                var root = doc.RootElement;
                return new RuleSpec(
                    Array(root, "repIds").Where(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out _)).Select(e => e.GetInt32()).ToList(),
                    Strings(root, "states"),
                    Strings(root, "zipPrefixes"),
                    Strings(root, "industries"));
            }
            catch (JsonException)
            {
                return Empty;
            }
        }

        private static IEnumerable<JsonElement> Array(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().ToList()
                : [];

        private static List<string> Strings(JsonElement root, string name) =>
            Array(root, name)
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!.Trim())
                .Where(s => s.Length > 0)
                .ToList();
    }
}
