namespace Forge.Core.Models;

/// <summary>One destructive DDL statement the schema reconcile refused to apply unattended.</summary>
public record DeployDestructiveStatementModel(int Number, string Statement);

/// <summary>
/// The disposition an operator owes before a halted upgrade can continue. Reaching the caller
/// only over the authenticated admin path — these statements name real columns and tables and
/// must never travel on the all-consoles broadcast.
/// </summary>
public record DeployApprovalModel(
    IReadOnlyList<DeployDestructiveStatementModel> Statements,
    bool PreMigrateCommitted,
    IReadOnlyList<string> Dispositions);

/// <summary>
/// A deploy job on the agent. <c>State</c> is running, succeeded, failed or halted-destructive;
/// the last means the gated CLI stopped before swapping anything and is waiting on
/// <see cref="NeedsApproval"/>.
/// </summary>
public record DeployJobModel(
    string Id,
    string Action,
    string? Service,
    string? Tag,
    string State,
    int? ExitCode,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    DeployApprovalModel? NeedsApproval,
    string? Reason,
    long LogSize);

/// <summary>
/// Outcome of asking the agent to start a job. <c>Status</c> is started, busy, rejected or
/// unavailable — an explicit result rather than an exception, so controllers stay free of the
/// try/catch the standards ratchet forbids.
/// </summary>
public record DeployJobStartResultModel(string Status, DeployJobModel? Job, string? Error);
