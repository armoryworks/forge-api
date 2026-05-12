namespace Forge.Core.Entities;

/// <summary>
/// Versioned, effective-dated form definitions. Every PDF extraction creates a new version.
/// Submissions link to a specific version so filled-out forms can be regenerated at any time.
/// Replaces StateFormDefinitionCache (flat cache) and ComplianceFormTemplate.FormDefinitionJson (overwritten).
/// </summary>
public class FormDefinitionVersion : BaseAuditableEntity
{
    /// <summary>
    /// FK to ComplianceFormTemplate for template-based forms (W-4, I-9, etc.).
    /// Null for state-specific forms resolved by StateCode alone.
    /// </summary>
    public int? TemplateId { get; set; }

    /// <summary>
    /// State code for state withholding forms (e.g., "ID", "CA").
    /// Null for non-state-specific templates.
    /// </summary>
    public string? StateCode { get; set; }

    /// <summary>
    /// The extracted form definition (ng-dynamic-forms compatible JSON).
    /// </summary>
    public string FormDefinitionJson { get; set; } = string.Empty;

    /// <summary>
    /// URL the source PDF was downloaded from.
    /// </summary>
    public string? SourceUrl { get; set; }

    /// <summary>
    /// SHA-256 hash of the source PDF bytes. Used to detect changes without re-extracting.
    /// </summary>
    public string? Sha256Hash { get; set; }

    /// <summary>
    /// When this version becomes the active form definition.
    /// </summary>
    public DateTimeOffset EffectiveDate { get; set; }

    /// <summary>
    /// When this version expires. Null = currently active (no successor yet).
    /// Set automatically when a new version is created for the same template/state.
    /// </summary>
    public DateTimeOffset? ExpirationDate { get; set; }

    /// <summary>
    /// Human-readable version label (e.g., "2026-03", "v2").
    /// </summary>
    public string Revision { get; set; } = string.Empty;

    /// <summary>
    /// When the PDF was extracted to produce this definition.
    /// </summary>
    public DateTimeOffset ExtractedAt { get; set; }

    /// <summary>
    /// Number of form fields in the definition.
    /// </summary>
    public int FieldCount { get; set; }

    /// <summary>
    /// Admin can deactivate a version without deleting it.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Serialized VisualComparisonResult JSON from the last visual comparison run.
    /// </summary>
    public string? VisualComparisonJson { get; set; }

    /// <summary>
    /// Denormalized average structural similarity score (0.0–1.0) for quick admin UI queries.
    /// </summary>
    public double? VisualSimilarityScore { get; set; }

    /// <summary>
    /// Denormalized pass/fail from the last visual comparison. Null if never compared.
    /// </summary>
    public bool? VisualComparisonPassed { get; set; }

    public ComplianceFormTemplate? Template { get; set; }
    public ICollection<ComplianceFormSubmission> Submissions { get; set; } = [];
}
