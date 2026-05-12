using System.Text.Json;

namespace Forge.Core.Models;

/// <summary>
/// Workflow Pattern Phase 3 — Save the current step's fields and (if the
/// step's completion gates pass) advance the pointer. Server applies fields
/// to the entity, re-evaluates the step's gates, and either advances
/// <c>current_step_id</c> to the next step or reports which gates are
/// still failing.
/// </summary>
public record PatchWorkflowStepRequestModel(
    string StepId,
    JsonElement Fields);
