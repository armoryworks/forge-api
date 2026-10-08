using System.Text.RegularExpressions;

namespace Forge.Api.Workflows;

/// <summary>
/// Builds the plain-language detail for a readiness 409, e.g.
/// "Finish these before this part can be active: Routing."
/// </summary>
public static partial class WorkflowReadinessMessage
{
    public static string FinishBefore(string entityType, string targetStatus, IEnumerable<string> validatorIds)
    {
        var names = validatorIds
            .Select(DisplayName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var subject = $"this {entityType.ToLowerInvariant()}";
        var status = targetStatus.ToLowerInvariant();
        return names.Count == 0
            ? $"Finish the required steps before {subject} can be {status}."
            : $"Finish these before {subject} can be {status}: {string.Join(", ", names)}.";
    }

    private static string DisplayName(string validatorId)
    {
        var name = validatorId.Length > 3
                   && validatorId.StartsWith("has", StringComparison.Ordinal)
                   && char.IsUpper(validatorId[3])
            ? validatorId[3..]
            : validatorId;
        if (string.Equals(name, "Bom", StringComparison.OrdinalIgnoreCase)) return "BOM";
        var words = WordBoundary().Replace(name, " ");
        return char.ToUpperInvariant(words[0]) + words[1..].ToLowerInvariant();
    }

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex WordBoundary();
}
