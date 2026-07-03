namespace Forge.Core.Entities;

/// <summary>
/// ai-fleet-orchestration D: marks an artifact as AI-generated so the UI can show a provenance
/// icon prompting extra human scrutiny (POs, SOs, customer notes, chat alerts, …). Polymorphic
/// (EntityType/EntityId), like ActivityLog — one marker per artifact.
/// </summary>
public class AiProvenance : BaseAuditableEntity
{
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }

    /// <summary>Which model/agent generated it (e.g. "gemma3:4b", "procurement-assistant").</summary>
    public string? Model { get; set; }

    public string? Notes { get; set; }
}
