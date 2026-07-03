using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Services;

/// <summary>
/// ai-fleet-orchestration D-2. Merges shipped baseline <c>.md</c> docs with a client override
/// directory; a client file at the same relative path shadows the baseline (client wins).
/// The result feeds the per-client RAG index (Tier 0). Weight-training tiers (LoRA/fine-tune)
/// and the multi-instance topology are infra-heavy follow-ups (see effort spec).
/// </summary>
public sealed class ClientDocResolver : IClientDocResolver
{
    public IReadOnlyList<ResolvedDoc> Resolve(string baselineDir, string clientOverrideDir)
    {
        var effective = new Dictionary<string, ResolvedDoc>(StringComparer.OrdinalIgnoreCase);

        // Baseline first, client second — the client pass overwrites same-key entries.
        foreach (var (dir, source) in new[] { (baselineDir, DocSource.Baseline), (clientOverrideDir, DocSource.Client) })
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                continue;

            foreach (var full in Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(dir, full).Replace('\\', '/');
                effective[rel] = new ResolvedDoc(rel, full, source);
            }
        }

        return effective.Values
            .OrderBy(d => d.RelativePath, StringComparer.Ordinal)
            .ToList();
    }
}
