namespace Forge.Core.Entities;

/// <summary>
/// A directed dependency: <see cref="ToStepKey"/> may not become Ready until <see cref="FromStepKey"/> is complete
/// (subject to the target's join policy). Rework edges are the only permitted cycles.
/// </summary>
public class SequenceEdgeDefinition : BaseEntity
{
    public int DefinitionId { get; set; }

    public SequenceDefinition? Definition { get; set; }

    public string FromStepKey { get; set; } = string.Empty;

    public string ToStepKey { get; set; } = string.Empty;

    /// <summary>True for a declared back-edge (rework loop). The net validator allows cycles only through these.</summary>
    public bool IsRework { get; set; }
}
