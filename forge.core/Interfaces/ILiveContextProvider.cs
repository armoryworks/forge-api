using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// ai-fleet-orchestration D-3 (hybrid RAG freshness): the "live-retrieve" half of the
/// embed-stable / live-retrieve-volatile split. Given the entities a RAG retrieval surfaced,
/// returns fresh volatile facts (stock, status, price, cost) via deterministic mapped queries —
/// no LLM tool-call loop — so answers reflect current data instead of stale embeddings.
/// </summary>
public interface ILiveContextProvider
{
    /// <summary>
    /// Returns a short "current facts" line for each referenced entity that has volatile data worth
    /// surfacing. Entities with nothing volatile (or unknown types) are omitted.
    /// </summary>
    Task<IReadOnlyList<LiveContextFact>> GetFactsAsync(
        IReadOnlyCollection<EntityReference> references, CancellationToken ct = default);
}
