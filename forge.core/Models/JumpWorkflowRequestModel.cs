namespace Forge.Core.Models;

/// <summary>
/// Workflow Pattern Phase 3 — Jump to a specific (current or earlier-completed)
/// step. Per D2, the user can navigate back to any completed step at any
/// time; future steps remain locked.
/// </summary>
public record JumpWorkflowRequestModel(string TargetStepId);
