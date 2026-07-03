namespace Forge.Core.Interfaces;

/// <summary>
/// ai-fleet-orchestration D: stamps an artifact as AI-generated (one marker per entity) and
/// answers whether an entity carries a provenance marker (for the UI provenance icon).
/// </summary>
public interface IAiProvenanceStamper
{
    Task StampAsync(string entityType, int entityId, string? model = null, CancellationToken ct = default);
    Task<bool> IsAiGeneratedAsync(string entityType, int entityId, CancellationToken ct = default);
}
