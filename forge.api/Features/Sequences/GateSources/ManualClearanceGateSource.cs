using Forge.Core.Enums;
using Forge.Core.Sequences;

namespace Forge.Api.Features.Sequences.GateSources;

/// <summary>Go once someone has recorded a clearance on the gate instance (<c>POST .../gates/{step}/{gate}/clear</c>). The record is the sign-off.</summary>
public class ManualClearanceGateSource : IGateSource
{
    public SequenceGateSourceType SourceType => SequenceGateSourceType.ManualClearance;

    public string? CustomKey => null;

    public Task<SequenceGateVerdictResult> EvaluateAsync(SequenceGateContext context, CancellationToken cancellationToken)
    {
        var gi = context.GateInstance;
        return Task.FromResult(gi.ClearedAt.HasValue
            ? SequenceGateVerdictResult.Go($"Cleared {gi.ClearedAt:u}")
            : SequenceGateVerdictResult.NoGo("Awaiting clearance"));
    }
}
