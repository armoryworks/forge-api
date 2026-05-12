namespace Forge.Api.Workflows;

/// <summary>
/// Workflow Pattern Phase 3 / Q4 — Step-level workflow audit event names.
/// Per-row entity edits already flow through AppDbContext.SaveChangesAsync;
/// these events cover what that loop cannot see.
/// </summary>
public static class WorkflowAuditEvents
{
    public const string EntityType = "WorkflowRun";

    public const string Started = "WorkflowStarted";
    public const string StepAdvanced = "WorkflowStepAdvanced";
    public const string JumpedTo = "WorkflowJumpedTo";
    public const string Completed = "WorkflowCompleted";
    public const string Abandoned = "WorkflowAbandoned";
    public const string ModeToggled = "WorkflowModeToggled";

    // Entity-level promotion (delegated by workflow Mark Complete; also
    // invoked directly from a detail page).
    public const string EntityStatusPromoted = "EntityStatusPromoted";
}
