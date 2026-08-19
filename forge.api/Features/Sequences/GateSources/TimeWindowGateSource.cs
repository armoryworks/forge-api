using Forge.Core.Enums;
using Forge.Core.Sequences;

namespace Forge.Api.Features.Sequences.GateSources;

/// <summary>Go while now is inside [notBefore, notAfter] (either bound optional). Re-evaluated by the clock job as boundaries pass.</summary>
public class TimeWindowGateSource : IGateSource
{
    public SequenceGateSourceType SourceType => SequenceGateSourceType.TimeWindow;

    public string? CustomKey => null;

    public Task<SequenceGateVerdictResult> EvaluateAsync(SequenceGateContext context, CancellationToken cancellationToken)
    {
        var cfg = SequenceGateConfig.Parse(context.Gate.ConfigJson);
        var notBefore = cfg.GetDate("notBefore");
        var notAfter = cfg.GetDate("notAfter");
        if (notBefore.HasValue && context.Now < notBefore.Value)
            return Task.FromResult(SequenceGateVerdictResult.NoGo($"Window opens {notBefore.Value:u}"));
        if (notAfter.HasValue && context.Now > notAfter.Value)
            return Task.FromResult(SequenceGateVerdictResult.NoGo($"Window closed {notAfter.Value:u}"));
        return Task.FromResult(SequenceGateVerdictResult.Go());
    }
}
