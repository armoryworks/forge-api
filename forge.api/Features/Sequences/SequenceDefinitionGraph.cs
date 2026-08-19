using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.Sequences;

/// <summary>Replaces a draft definition's steps/edges/gates from a request model (whole-document semantics).</summary>
public static class SequenceDefinitionGraph
{
    public static void Apply(SequenceDefinition def, SequenceDefinitionRequestModel m)
    {
        def.Name = m.Name.Trim();
        def.Description = string.IsNullOrWhiteSpace(m.Description) ? null : m.Description.Trim();
        def.SubjectEntityType = string.IsNullOrWhiteSpace(m.SubjectEntityType) ? null : m.SubjectEntityType.Trim();

        def.Steps.Clear();
        foreach (var s in m.Steps ?? [])
            def.Steps.Add(new SequenceStepDefinition
            {
                Key = s.Key.Trim(), Name = s.Name.Trim(), Description = s.Description, SortOrder = s.SortOrder,
                JoinPolicy = s.JoinPolicy, MaxDwellMinutes = s.MaxDwellMinutes, DwellExpiryAction = s.DwellExpiryAction,
                EscalateRole = s.EscalateRole,
            });

        def.Edges.Clear();
        foreach (var e in m.Edges ?? [])
            def.Edges.Add(new SequenceEdgeDefinition { FromStepKey = e.FromStepKey.Trim(), ToStepKey = e.ToStepKey.Trim(), IsRework = e.IsRework });

        def.Gates.Clear();
        foreach (var g in m.Gates ?? [])
            def.Gates.Add(new SequenceGateDefinition
            {
                StepKey = g.StepKey.Trim(), Key = g.Key.Trim(), Name = g.Name.Trim(), SourceType = g.SourceType,
                ConfigJson = string.IsNullOrWhiteSpace(g.ConfigJson) ? "{}" : g.ConfigJson, ExpiryAction = g.ExpiryAction,
                EscalateRole = g.EscalateRole,
            });
    }
}
