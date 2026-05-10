using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Entities;
using QBEngineer.Core.Interfaces;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Jobs;

/// <summary>
/// Phase 1r / Batch 10 — nightly Lead.IcpScore recompute.
///
/// Evaluates each dimension of the *default* rubric against every active
/// (non-Converted, non-Lost) lead and writes the normalized score back
/// to Lead.IcpScore. The worker queue surfaces this column so reps work
/// the highest-fit leads first.
///
/// Match algorithm (v1, conservative):
///   - dimension.MatchSpec is a JSON array of strings → substring match
///     (case-insensitive) against the lead's CompanyName / Email-domain /
///     Source. If the lead has an Account, also against the account's
///     Industry / State / SizeBracket.
///   - dimension.MatchSpec is `{ "industries": [...] }` → exact match
///     against the linked account's Industry.
///   - dimension.MatchSpec is `{ "states": [...] }` → exact match
///     against the linked account's State.
///   - dimension.MatchSpec is `{ "min": N, "max": M }` → numeric range
///     match against the linked account's SizeBracket if it's numeric.
///
/// Anything else short-circuits to "no match". Admins can hand-tune the
/// MatchSpec JSON in the ICP Rubric admin and the next nightly run
/// picks it up.
/// </summary>
public class ComputeLeadIcpScoresJob(
    AppDbContext db,
    IClock clock,
    ILogger<ComputeLeadIcpScoresJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var rubric = await db.IcpRubrics.AsNoTracking()
            .Include(r => r.Dimensions)
            .FirstOrDefaultAsync(r => r.IsActive && r.IsDefault, ct);

        if (rubric is null)
        {
            logger.LogInformation("ICP scoring: no default rubric configured; skipping.");
            return;
        }

        var dims = rubric.Dimensions.ToList();
        if (dims.Count == 0)
        {
            logger.LogInformation("ICP scoring: default rubric '{Name}' has no dimensions; skipping.", rubric.Name);
            return;
        }

        var maxScore = dims.Where(d => d.Weight > 0).Sum(d => d.Weight);
        if (maxScore == 0)
        {
            logger.LogWarning("ICP scoring: default rubric '{Name}' has zero positive weight; skipping.", rubric.Name);
            return;
        }

        // Pull active leads + their accounts in batches so a 50k-lead
        // install doesn't blow memory.
        const int batchSize = 500;
        var totalProcessed = 0;
        var startedAt = clock.UtcNow;

        var leadIds = await db.Leads.AsNoTracking()
            .Where(l => l.Status != Core.Enums.LeadStatus.Converted && l.Status != Core.Enums.LeadStatus.Lost)
            .Select(l => l.Id)
            .ToListAsync(ct);

        for (var i = 0; i < leadIds.Count; i += batchSize)
        {
            if (ct.IsCancellationRequested) break;
            var batch = leadIds.Skip(i).Take(batchSize).ToList();

            var leadsBatch = await db.Leads
                .Where(l => batch.Contains(l.Id))
                .Include(l => l.Account)
                .ToListAsync(ct);

            foreach (var lead in leadsBatch)
            {
                lead.IcpScore = ComputeScore(lead, dims, maxScore);
                totalProcessed++;
            }

            await db.SaveChangesAsync(ct);
        }

        var elapsed = clock.UtcNow - startedAt;
        logger.LogInformation(
            "ICP scoring: recomputed {Count} leads against rubric '{Name}' in {Elapsed:F2}s.",
            totalProcessed, rubric.Name, elapsed.TotalSeconds);
    }

    private static int ComputeScore(Lead lead, List<IcpDimension> dims, int maxScore)
    {
        var hitWeight = 0;
        foreach (var dim in dims)
        {
            if (Matches(lead, dim))
            {
                hitWeight += dim.Weight;
            }
        }
        // Clamp to [0, 100] — negative weights can push below zero.
        var normalized = (int)Math.Round((double)hitWeight / maxScore * 100.0);
        return Math.Clamp(normalized, 0, 100);
    }

    private static bool Matches(Lead lead, IcpDimension dim)
    {
        if (string.IsNullOrWhiteSpace(dim.MatchSpec)) return false;
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(dim.MatchSpec);
            var root = doc.RootElement;

            // Array of strings — substring match on lead/account fields.
            if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var candidates = CollectLeadStrings(lead);
                foreach (var v in root.EnumerateArray())
                {
                    if (v.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                    var needle = v.GetString();
                    if (string.IsNullOrWhiteSpace(needle)) continue;
                    if (candidates.Any(c => c.Contains(needle, StringComparison.OrdinalIgnoreCase))) return true;
                }
                return false;
            }

            // Object — keyed match against specific lead/account fields.
            if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (root.TryGetProperty("industries", out var industries) && lead.Account?.Industry is { } industry)
                {
                    foreach (var v in industries.EnumerateArray())
                    {
                        if (v.ValueKind == System.Text.Json.JsonValueKind.String &&
                            string.Equals(v.GetString(), industry, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                if (root.TryGetProperty("states", out var states) && lead.Account?.State is { } state)
                {
                    foreach (var v in states.EnumerateArray())
                    {
                        if (v.ValueKind == System.Text.Json.JsonValueKind.String &&
                            string.Equals(v.GetString(), state, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                if (root.TryGetProperty("min", out var minProp) && root.TryGetProperty("max", out var maxProp)
                    && minProp.TryGetInt32(out var min) && maxProp.TryGetInt32(out var max)
                    && int.TryParse(lead.Account?.SizeBracket, out var size))
                {
                    return size >= min && size <= max;
                }
            }
        }
        catch
        {
            // Malformed JSON — treat as "no match"; admin will see the
            // raw spec in the rubric editor and can fix it.
        }
        return false;
    }

    private static List<string> CollectLeadStrings(Lead lead)
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(lead.CompanyName)) list.Add(lead.CompanyName);
        if (!string.IsNullOrWhiteSpace(lead.Source)) list.Add(lead.Source);
        if (!string.IsNullOrWhiteSpace(lead.Email)) list.Add(lead.Email);
        if (lead.Account is { } acc)
        {
            if (!string.IsNullOrWhiteSpace(acc.Industry)) list.Add(acc.Industry);
            if (!string.IsNullOrWhiteSpace(acc.State)) list.Add(acc.State);
            if (!string.IsNullOrWhiteSpace(acc.SizeBracket)) list.Add(acc.SizeBracket);
            if (!string.IsNullOrWhiteSpace(acc.Name)) list.Add(acc.Name);
        }
        return list;
    }
}
