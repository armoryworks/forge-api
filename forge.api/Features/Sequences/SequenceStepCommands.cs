using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Sequences;

/// <summary>Guards shared by the step/gate commands.</summary>
public static class SequenceStepCommands
{
    public static async Task<SequenceInstance> LoadRunning(AppDbContext db, int instanceId, CancellationToken ct)
    {
        var i = await db.SequenceInstances.WithGraph().FirstOrDefaultAsync(x => x.Id == instanceId && x.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException($"Sequence instance {instanceId} not found.");
        if (i.Status != SequenceInstanceStatus.Running)
            throw new InvalidOperationException($"Sequence instance {instanceId} is {i.Status}.");
        return i;
    }

    public static SequenceStepInstance Step(SequenceInstance i, string stepKey) =>
        i.Steps.FirstOrDefault(s => s.StepKey == stepKey)
        ?? throw new KeyNotFoundException($"Step '{stepKey}' is not part of this sequence.");

    public static SequenceGateInstance Gate(SequenceInstance i, string stepKey, string gateKey) =>
        i.Gates.FirstOrDefault(g => g.StepKey == stepKey && g.GateKey == gateKey)
        ?? throw new KeyNotFoundException($"Gate '{gateKey}' on step '{stepKey}' is not part of this sequence.");
}
