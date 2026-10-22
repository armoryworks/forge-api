using Forge.Core.Enums;
using Forge.Core.Routing;

namespace Forge.Core.Interfaces;

public interface IStepGateService
{
    /// <summary>The gating mode for a part: its own override, else the shop setting, else Off.</summary>
    Task<StepGatingMode> ModeAsync(int? partId, CancellationToken ct);

    /// <summary>
    /// Checks a progress update against step gating. Returns the outcome when it may go ahead (with the
    /// acknowledgement or override to log); throws when the gate stops it.
    /// </summary>
    Task<StepGateOutcome> EnforceAsync(StepGateRequest request, CancellationToken ct);
}
