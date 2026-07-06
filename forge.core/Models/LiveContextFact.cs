namespace Forge.Core.Models;

/// <summary>
/// ai-fleet-orchestration D-3 (hybrid RAG freshness): a fresh, volatile-fact snippet for one entity,
/// injected into RAG answer context so the model sees current data (on-hand, status, cost) rather
/// than the stable/semantic content frozen in its embeddings.
/// </summary>
public record LiveContextFact(string EntityType, int EntityId, string Facts);
