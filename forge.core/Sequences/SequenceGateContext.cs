using Forge.Core.Entities;

namespace Forge.Core.Sequences;

/// <summary>Everything a gate source may look at when answering: the definition, the run, the gate's own instance row, and "now".</summary>
public sealed record SequenceGateContext(
    SequenceDefinition Definition,
    SequenceGateDefinition Gate,
    SequenceInstance Instance,
    SequenceGateInstance GateInstance,
    DateTimeOffset Now);
