namespace Forge.Core.Models;

/// <summary>
/// A polymorphic entity pointer (EntityType + EntityId) — the same shape ActivityLog, StatusEntry,
/// AiProvenance, and DocumentEmbedding use. Value semantics, so it de-duplicates cleanly in a set.
/// </summary>
public record EntityReference(string EntityType, int EntityId);
