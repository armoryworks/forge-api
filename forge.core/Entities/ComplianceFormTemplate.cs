using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class ComplianceFormTemplate : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public ComplianceFormType FormType { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public string? Sha256Hash { get; set; }
    public bool IsAutoSync { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public bool RequiresIdentityDocs { get; set; }
    public int? DocuSealTemplateId { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public int? ManualOverrideFileId { get; set; }
    public bool BlocksJobAssignment { get; set; }
    public string ProfileCompletionKey { get; set; } = string.Empty;

    /// <summary>
    /// JSON mapping from dynamic form field IDs to AcroForm field names in the government PDF.
    /// When set, the backend fills the official PDF with collected form data before DocuSeal signing.
    /// </summary>
    public string? AcroFieldMapJson { get; set; }

    /// <summary>
    /// ID of the blank government PDF stored in MinIO (the template to fill).
    /// Used together with AcroFieldMapJson to produce a pre-filled PDF for signing.
    /// </summary>
    public int? FilledPdfTemplateId { get; set; }

    public FileAttachment? ManualOverrideFile { get; set; }
    public FileAttachment? FilledPdfTemplate { get; set; }
    public ICollection<ComplianceFormSubmission> Submissions { get; set; } = [];
    public ICollection<FormDefinitionVersion> FormDefinitionVersions { get; set; } = [];
}
