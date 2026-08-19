using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// A named go/no-go condition attached to a step. Gates are their own objects (not step properties) so the same
/// source type can be attached to any step in any definition, and a step can carry several.
/// </summary>
public class SequenceGateDefinition : BaseEntity
{
    public int DefinitionId { get; set; }

    public SequenceDefinition? Definition { get; set; }

    public string StepKey { get; set; } = string.Empty;

    /// <summary>Stable key within the step, e.g. "materials", "first-article".</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public SequenceGateSourceType SourceType { get; set; }

    /// <summary>Source-specific configuration; shape documented per <see cref="SequenceGateSourceType"/>.</summary>
    public string ConfigJson { get; set; } = "{}";

    /// <summary>What happens when a clock this gate depends on expires.</summary>
    public SequenceExpiryAction ExpiryAction { get; set; } = SequenceExpiryAction.Block;

    public string? EscalateRole { get; set; }
}
