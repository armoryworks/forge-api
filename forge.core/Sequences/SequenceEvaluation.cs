using Forge.Core.Entities;

namespace Forge.Core.Sequences;

/// <summary>Outcome of one evaluator pass: the events to append and the flags callers react to.</summary>
public sealed class SequenceEvaluation
{
    public List<SequenceEvent> Events { get; } = [];

    /// <summary>Step keys that transitioned Pending → Ready in this pass (domain-event candidates).</summary>
    public List<string> NewlyReady { get; } = [];

    /// <summary>Step keys that are blocked after this pass (predecessors satisfied, ≥1 gate not Go).</summary>
    public List<string> Blocked { get; } = [];

    /// <summary>True when the pass drove the instance to Completed.</summary>
    public bool CompletedInstance { get; set; }

    public bool Changed => Events.Count > 0;
}
