using Forge.Core.Enums;

namespace Forge.Core.Sequences;

/// <summary>A gate source's answer: the verdict plus the human-readable reason shown as "blocked because ...".</summary>
public sealed record SequenceGateVerdictResult(SequenceGateVerdict Verdict, string? Reason = null)
{
    public static SequenceGateVerdictResult Go(string? reason = null) => new(SequenceGateVerdict.Go, reason);
    public static SequenceGateVerdictResult NoGo(string reason) => new(SequenceGateVerdict.NoGo, reason);
    public static SequenceGateVerdictResult Unknown(string reason) => new(SequenceGateVerdict.Unknown, reason);
}
