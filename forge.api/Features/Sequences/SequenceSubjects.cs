namespace Forge.Api.Features.Sequences;

/// <summary>Subject entity types a sequence can run against. Known types are stored in their canonical casing so
/// "job" and "Job" name the same subject; any other non-blank type is kept as typed (trimmed).</summary>
public static class SequenceSubjects
{
    public const string Job = "Job";

    /// <summary>Lower-cased <see cref="Job"/>, for case-insensitive comparisons inside EF queries.</summary>
    public const string JobLower = "job";

    private static readonly string[] Known = [Job, "Part", "SalesOrder", "PurchaseOrder", "Quote", "Customer", "Vendor", "Asset", "Lot"];

    public static string? Canonical(string? subjectEntityType)
    {
        if (string.IsNullOrWhiteSpace(subjectEntityType))
            return null;
        var trimmed = subjectEntityType.Trim();
        return Known.FirstOrDefault(k => string.Equals(k, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
    }

    public static bool IsJob(string? subjectEntityType) =>
        string.Equals(subjectEntityType?.Trim(), Job, StringComparison.OrdinalIgnoreCase);
}
