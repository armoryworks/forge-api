using Forge.Core.Models;

namespace Forge.Api.Features.Parts;

/// <summary>
/// Thrown when a step renumber or reorder would leave a "must follow" link pointing at a step that no longer comes
/// first. The exception middleware maps this to 409 with problem code "predecessor-conflict" and the links in
/// "conflicts"; the client asks the user and resends with RemoveConflictingPredecessors=true.
/// </summary>
public class PredecessorConflictException(IReadOnlyList<PredecessorConflictResponseModel> conflicts)
    : Exception(Describe(conflicts))
{
    public const string Code = "predecessor-conflict";

    public IReadOnlyList<PredecessorConflictResponseModel> Conflicts { get; } = conflicts;

    private static string Describe(IReadOnlyList<PredecessorConflictResponseModel> conflicts)
        => "This order breaks " + (conflicts.Count == 1 ? "a step link" : $"{conflicts.Count} step links") + ": "
            + string.Join(", ", conflicts.Select(c => $"Op {c.StepNumber} must follow Op {c.PredecessorStepNumber}"))
            + ". Remove the links or choose another order.";
}
