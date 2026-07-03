using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// ai-fleet-orchestration D-2: resolves the effective per-client RAG doc set by merging the
/// shipped baseline docs with a client override directory — a client file at the same relative
/// path shadows the baseline (client wins), and client-only files are added.
/// </summary>
public interface IClientDocResolver
{
    IReadOnlyList<ResolvedDoc> Resolve(string baselineDir, string clientOverrideDir);
}
