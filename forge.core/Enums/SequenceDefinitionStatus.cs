namespace Forge.Core.Enums;

/// <summary>Lifecycle of a <see cref="Entities.SequenceDefinition"/> version. Draft is editable; Published is
/// immutable and startable; Retired can no longer start new instances (in-flight ones keep running).</summary>
public enum SequenceDefinitionStatus
{
    Draft,
    Published,
    Retired,
}
