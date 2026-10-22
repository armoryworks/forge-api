namespace Forge.Core.Models;

/// <summary>
/// The steps of a guided run the user has left with Skip and not finished
/// since, so the step rail can still flag them after a reload or resume.
/// Replaces the run's previous list; an empty list clears it.
/// </summary>
public record SaveWorkflowSkippedStepsRequestModel(IReadOnlyList<string> StepIds);
