namespace Forge.Core.Models;

/// <summary>
/// PUT payload that replaces the full set of SO-line quantity allocations on a
/// stage. The sum of a line's quantity across ALL stages of the order must not
/// exceed that line's ordered quantity (enforced in the handler).
/// </summary>
public record AssignStageLinesRequestModel(
    List<StageLineAllocationModel> Lines);
