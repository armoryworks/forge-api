namespace Forge.Core.Entities.Compliance;

/// <summary>
/// regulated-parts-safety C-1: a mandatory-field rule contributed by a
/// <see cref="ComplianceProfile"/> — "field X is required at process step Y". The effective
/// rule set is the union across the shop's active profiles.
/// </summary>
public class ComplianceFieldRule : BaseAuditableEntity
{
    public int ComplianceProfileId { get; set; }
    public ComplianceProfile ComplianceProfile { get; set; } = null!;

    /// <summary>Field the rule requires (e.g. "lotNumber", "coo", "sdsDocument").</summary>
    public string FieldKey { get; set; } = string.Empty;

    /// <summary>Named process step where the field becomes mandatory (e.g. "part.create", "job.qc").</summary>
    public string ProcessStep { get; set; } = string.Empty;

    /// <summary>Optional condition expression narrowing when the rule applies (null = always).</summary>
    public string? Condition { get; set; }
}
