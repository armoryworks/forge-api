using System.Text.Json;

using Forge.Core.Models;

namespace Forge.Api.Workflows;

/// <summary>
/// Workflow Pattern Phase 3 — Parses the StepsJson payload and exposes the
/// step ordering / completion-gate references needed by the run lifecycle
/// handlers. Pure code (no DB access); each handler reads StepsJson from
/// the pinned definition row and uses this to compute next step / jump
/// rules / required-step set.
/// </summary>
internal static class WorkflowStepHelper
{
    public static IReadOnlyList<WorkflowStepDefinition> ParseSteps(string stepsJson)
    {
        var result = new List<WorkflowStepDefinition>();
        if (string.IsNullOrWhiteSpace(stepsJson)) return result;
        try
        {
            using var doc = JsonDocument.Parse(stepsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;

                var id = element.TryGetProperty("id", out var idP) && idP.ValueKind == JsonValueKind.String
                    ? idP.GetString() ?? ""
                    : "";
                var labelKey = element.TryGetProperty("labelKey", out var lk) && lk.ValueKind == JsonValueKind.String
                    ? lk.GetString() ?? ""
                    : "";
                var componentName = element.TryGetProperty("componentName", out var cn) && cn.ValueKind == JsonValueKind.String
                    ? cn.GetString() ?? ""
                    : "";
                var required = element.TryGetProperty("required", out var rq) && rq.ValueKind == JsonValueKind.True;
                var gates = new List<string>();
                if (element.TryGetProperty("completionGates", out var cg) && cg.ValueKind == JsonValueKind.Array)
                {
                    foreach (var gate in cg.EnumerateArray())
                        if (gate.ValueKind == JsonValueKind.String)
                            gates.Add(gate.GetString() ?? "");
                }
                if (string.IsNullOrEmpty(id)) continue;
                result.Add(new WorkflowStepDefinition(id, labelKey, componentName, required, gates));
            }
        }
        catch (JsonException) { /* fall through; empty result */ }
        return result;
    }

    /// <summary>Returns the index of <paramref name="stepId"/> in the ordered list, or -1.</summary>
    public static int IndexOf(IReadOnlyList<WorkflowStepDefinition> steps, string? stepId)
    {
        if (string.IsNullOrEmpty(stepId)) return -1;
        for (var i = 0; i < steps.Count; i++)
            if (string.Equals(steps[i].Id, stepId, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }
}
