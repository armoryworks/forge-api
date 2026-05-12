namespace Forge.Core.Models;

/// <summary>
/// Compact summary of an in-progress workflow run, embedded in list-row
/// projections (e.g. <see cref="PartListResponseModel"/>) so the UI can
/// surface a "this row has an unfinished workflow" indicator without an
/// extra round-trip per row.
///
/// <para>Returned only for runs where <c>CompletedAt IS NULL</c> AND
/// <c>AbandonedAt IS NULL</c>. Null on the parent record means "no
/// in-progress workflow against this entity."</para>
/// </summary>
public record PendingWorkflowSummary(
    int RunId,
    string DefinitionId,
    string? CurrentStepId,
    string Mode,
    DateTimeOffset LastActivityAt);
