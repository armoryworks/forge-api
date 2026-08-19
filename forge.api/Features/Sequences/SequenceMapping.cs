using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Core.Sequences;

namespace Forge.Api.Features.Sequences;

/// <summary>Entity → response-model mapping for the Sequences feature (Blocked is derived here, never read from a column).</summary>
public static class SequenceMapping
{
    public static SequenceDefinitionResponseModel ToModel(SequenceDefinition d) => new(
        d.Id, d.Code, d.Version, d.Name, d.Description, d.SubjectEntityType, d.Status, d.AutoStartOnSubjectCreate, d.PublishedAt,
        d.Steps.OrderBy(s => s.SortOrder).ThenBy(s => s.Key).Select(s => new SequenceStepDefinitionModel(
            s.Key, s.Name, s.Description, s.SortOrder, s.JoinPolicy, s.MaxDwellMinutes, s.DwellExpiryAction, s.EscalateRole)).ToList(),
        d.Edges.OrderBy(e => e.FromStepKey).ThenBy(e => e.ToStepKey).Select(e => new SequenceEdgeDefinitionModel(e.FromStepKey, e.ToStepKey, e.IsRework)).ToList(),
        d.Gates.OrderBy(g => g.StepKey).ThenBy(g => g.Key).Select(g => new SequenceGateDefinitionModel(
            g.StepKey, g.Key, g.Name, g.SourceType, g.ConfigJson, g.ExpiryAction, g.EscalateRole)).ToList(),
        d.CreatedAt, d.UpdatedAt);

    public static SequenceInstanceResponseModel ToModel(SequenceInstance i)
    {
        var def = i.Definition ?? throw new InvalidOperationException("Instance loaded without its definition.");
        var net = new SequenceNet(def);
        var stepDefs = def.Steps.ToDictionary(s => s.Key, StringComparer.Ordinal);
        var gateDefs = def.Gates.ToDictionary(g => (g.StepKey, g.Key));
        var gates = i.Gates.ToDictionary(g => (g.StepKey, g.GateKey));

        var steps = i.Steps
            .OrderBy(s => stepDefs.TryGetValue(s.StepKey, out var d) ? d.SortOrder : int.MaxValue).ThenBy(s => s.StepKey)
            .Select(s =>
            {
                stepDefs.TryGetValue(s.StepKey, out var d);
                var blocked = SequenceEvaluator.IsBlocked(net, i, s.StepKey);
                return new SequenceStepInstanceResponseModel(
                    s.StepKey, d?.Name ?? s.StepKey, d?.SortOrder ?? 0, s.Status, blocked,
                    blocked && d is not null ? SequenceEvaluator.BlockedReason(net, d, gates) : null,
                    net.PredecessorsOf(s.StepKey).ToList(),
                    s.ReadyAt, s.StartedAt, s.StartedByUserId, s.CompletedAt, s.CompletedByUserId, s.SkipReason,
                    s.DwellExpiresAt, s.DwellFiredAt);
            }).ToList();

        var gateModels = i.Gates.OrderBy(g => g.StepKey).ThenBy(g => g.GateKey).Select(g =>
        {
            gateDefs.TryGetValue((g.StepKey, g.GateKey), out var gd);
            return new SequenceGateInstanceResponseModel(g.StepKey, g.GateKey, gd?.Name ?? g.GateKey,
                gd?.SourceType ?? default, g.Verdict, g.Reason, g.LastEvaluatedAt, g.ClearedAt, g.ClearedByUserId,
                g.OverriddenAt, g.OverriddenByUserId, g.OverrideReason);
        }).ToList();

        return new SequenceInstanceResponseModel(i.Id, i.DefinitionId, def.Code, def.Version, def.Name,
            i.SubjectEntityType, i.SubjectEntityId, i.Status, i.StartedAt, i.StartedByUserId, i.CompletedAt,
            i.CancelledAt, i.CancelReason, i.Version, steps, gateModels);
    }

    public static SequenceEventResponseModel ToModel(SequenceEvent e) =>
        new(e.Id, e.Type, e.StepKey, e.GateKey, e.PayloadJson, e.OccurredAt, e.ActorUserId);

    public static SequenceResourceClockResponseModel ToModel(SequenceResourceClock c, DateTimeOffset now) =>
        new(c.Id, c.ResourceType, c.ResourceId, c.ExpiresAt, c.ExpiryAction, c.EscalateRole, c.Note, c.FiredAt, c.ExpiresAt <= now);
}
