namespace Forge.Core.Sequences;

/// <summary>
/// Subject entity types the engine knows how to resolve. Known types are stored in their canonical spelling whatever
/// case they were typed in; any other type is kept as typed (trimmed) so custom gate sources can still use it.
/// </summary>
public static class SequenceSubjectTypes
{
    public const string Job = "Job";

    public static readonly IReadOnlyList<string> Known = [Job];

    /// <summary>Trimmed type, in canonical spelling when it is a known type; null when blank.</summary>
    public static string? Normalize(string? subjectEntityType)
    {
        if (string.IsNullOrWhiteSpace(subjectEntityType)) return null;
        var trimmed = subjectEntityType.Trim();
        return Known.FirstOrDefault(k => string.Equals(k, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
    }

    public static bool IsJob(string? subjectEntityType) =>
        string.Equals(subjectEntityType?.Trim(), Job, StringComparison.OrdinalIgnoreCase);
}
