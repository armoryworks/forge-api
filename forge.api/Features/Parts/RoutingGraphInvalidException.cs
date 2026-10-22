using Forge.Core.Models;

namespace Forge.Api.Features.Parts;

/// <summary>
/// Thrown when a whole-routing save breaks the graph rules. The exception middleware maps this to 422 with problem
/// code "routing-graph-invalid" and the problems in "issues"; nothing is written.
/// </summary>
public class RoutingGraphInvalidException(IReadOnlyList<RoutingGraphIssueResponseModel> issues)
    : Exception(Describe(issues))
{
    public const string Code = "routing-graph-invalid";

    public IReadOnlyList<RoutingGraphIssueResponseModel> Issues { get; } = issues;

    private static string Describe(IReadOnlyList<RoutingGraphIssueResponseModel> issues)
        => "This routing can't be saved: "
            + string.Join(", ", issues.Take(5).Select(i => i.PredecessorStepNumber is { } predecessor
                ? $"Op {i.StepNumber} can't follow Op {predecessor}"
                : $"Op {i.StepNumber?.ToString() ?? "?"} ({i.Kind})"))
            + (issues.Count > 5 ? $" and {issues.Count - 5} more" : string.Empty)
            + ".";
}
