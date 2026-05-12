namespace Forge.Core.Entities;

/// <summary>
/// Phase 1r / Batch 10 — admin-configured Ideal Customer Profile rubric.
/// Each <see cref="IcpRubric"/> has many <see cref="IcpDimension"/>s
/// (industry / size / location / certs / etc.) with point weights.
/// A lead's score is the sum of dimension hits, normalized to 0-100.
///
/// The score surfaces in the worker queue so reps work the highest-fit
/// leads first. Computation happens at scoring time, not query time,
/// so the LeadScore column on Lead is the cached result.
/// </summary>
public class IcpRubric : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }

    public ICollection<IcpDimension> Dimensions { get; set; } = [];
}

public class IcpDimension : BaseAuditableEntity
{
    public int IcpRubricId { get; set; }
    /// <summary>Field on Lead or Customer the dimension matches against (e.g. "industry", "employeeCount", "state").</summary>
    public string FieldKey { get; set; } = string.Empty;
    /// <summary>Free-form description so admins remember what this dimension targets.</summary>
    public string? Label { get; set; }
    /// <summary>Match-value spec stored as JSON — supports value lists, ranges, regex.</summary>
    public string? MatchSpec { get; set; }
    /// <summary>Point weight when a lead matches. Negative weights penalize.</summary>
    public int Weight { get; set; }

    public IcpRubric Rubric { get; set; } = null!;
}
