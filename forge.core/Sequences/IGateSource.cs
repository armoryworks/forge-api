using Forge.Core.Enums;

namespace Forge.Core.Sequences;

/// <summary>
/// A pluggable go/no-go evaluator. Built-in sources cover the engine's own concepts; modules register additional
/// implementations with <see cref="SourceType"/> = Custom and a <see cref="CustomKey"/> matched against
/// the gate's <c>config.key</c>. Implementations must be idempotent and side-effect free — they may be called
/// on every re-evaluation.
/// </summary>
public interface IGateSource
{
    SequenceGateSourceType SourceType { get; }

    /// <summary>For Custom sources, the key a gate config names to select this source; null otherwise.</summary>
    string? CustomKey { get; }

    Task<SequenceGateVerdictResult> EvaluateAsync(SequenceGateContext context, CancellationToken cancellationToken);
}
