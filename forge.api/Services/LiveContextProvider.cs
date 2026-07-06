using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// ai-fleet-orchestration D-3: deterministic live-fact provider for RAG answers. Volatile facts are
/// resolved here (the indexer embeds only stable/semantic text — descriptions, specs, notes). Batched
/// and AsNoTracking per the efficiency rules.
///
/// Only entity types the RAG index actually surfaces are worth handling; Part is the primary win
/// (on-hand, status, and unit cost all go stale in embeddings). Extend the switch as more
/// volatile-bearing types get indexed (Job stage, Customer open-order rollups, …).
/// </summary>
public sealed class LiveContextProvider(AppDbContext db) : ILiveContextProvider
{
    public async Task<IReadOnlyList<LiveContextFact>> GetFactsAsync(
        IReadOnlyCollection<EntityReference> references, CancellationToken ct = default)
    {
        if (references.Count == 0)
            return [];

        var facts = new List<LiveContextFact>();

        var partIds = references
            .Where(r => r.EntityType == "Part")
            .Select(r => r.EntityId)
            .Distinct()
            .ToList();

        if (partIds.Count > 0)
            facts.AddRange(await GetPartFactsAsync(partIds, ct));

        return facts;
    }

    private async Task<IReadOnlyList<LiveContextFact>> GetPartFactsAsync(List<int> partIds, CancellationToken ct)
    {
        var parts = await db.Parts.AsNoTracking()
            .Where(p => partIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Status, p.ManualCostOverride })
            .ToListAsync(ct);

        // On-hand + reserved rolled up per part in one grouped query. BinContent is polymorphic
        // (EntityType "part" + EntityId — not a Part FK); RemovedAt == null == currently in a bin.
        var stock = await db.BinContents.AsNoTracking()
            .Where(b => b.EntityType == "part" && partIds.Contains(b.EntityId) && b.RemovedAt == null)
            .GroupBy(b => b.EntityId)
            .Select(g => new
            {
                PartId = g.Key,
                OnHand = g.Sum(x => x.Quantity),
                Reserved = g.Sum(x => x.ReservedQuantity),
            })
            .ToListAsync(ct);

        var stockById = stock.ToDictionary(s => s.PartId);

        var results = new List<LiveContextFact>(parts.Count);
        foreach (var part in parts)
        {
            var bits = new List<string>();

            if (stockById.TryGetValue(part.Id, out var s))
            {
                var available = s.OnHand - s.Reserved;
                bits.Add(s.Reserved > 0
                    ? $"on-hand {Fmt(s.OnHand)} (available {Fmt(available)} after {Fmt(s.Reserved)} reserved)"
                    : $"on-hand {Fmt(s.OnHand)}");
            }
            else
            {
                bits.Add("on-hand 0");
            }

            bits.Add($"status {part.Status}");

            if (part.ManualCostOverride is decimal cost)
                bits.Add($"unit cost {Fmt(cost)} (manual override)");

            results.Add(new LiveContextFact("Part", part.Id, string.Join("; ", bits)));
        }

        return results;
    }

    private static string Fmt(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
