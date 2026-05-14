
namespace Forge.Core.Entities;

public class ComplianceFormSubmission : BaseAuditableEntity
{
    public int TemplateId { get; set; }
    public int UserId { get; set; }
    public int? DocuSealSubmissionId { get; set; }
    public ComplianceSubmissionStatus Status { get; set; } = ComplianceSubmissionStatus.Pending;
    public DateTimeOffset? SignedAt { get; set; }
    public int? SignedPdfFileId { get; set; }
    public string? DocuSealSubmitUrl { get; set; }
    public string? FormDataJson { get; set; }

    /// <summary>
    /// Pins this submission to the exact form definition version it was filled against.
    /// Allows regeneration of the form with the original layout at any time.
    /// </summary>
    public int? FormDefinitionVersionId { get; set; }

    // ── PDF Fill & Signing fields ──────────────────────────────────────────

    /// <summary>
    /// The AcroForm-filled (but not yet signed) government PDF stored in MinIO.
    /// Populated after form data collection, before DocuSeal signing ceremony.
    /// </summary>
    public int? FilledPdfFileId { get; set; }

    // ── I-9 specific fields ───────────────────────────────────────────────

    /// <summary>When employee completed DocuSeal signing of Section 1.</summary>
    public DateTimeOffset? I9Section1SignedAt { get; set; }

    /// <summary>When employer completed DocuSeal signing of Section 2.</summary>
    public DateTimeOffset? I9Section2SignedAt { get; set; }

    /// <summary>Employer (manager/admin) who reviewed and signed Section 2.</summary>
    public int? I9EmployerUserId { get; set; }

    /// <summary>"A" or "B+C" — document list chosen by employee for Section 2.</summary>
    public string? I9DocumentListType { get; set; }

    /// <summary>
    /// Section 2 document data (List A or B+C), stored as JSON.
    /// Includes document types, numbers, issuing authority, expiration dates.
    /// Also stores start date (first day of employment) for Section 2 attestation.
    /// </summary>
    public string? I9DocumentDataJson { get; set; }

    /// <summary>
    /// Deadline by which Section 2 must be completed (first day of work + 3 business days).
    /// Set when Section 1 is signed.
    /// </summary>
    public DateTimeOffset? I9Section2OverdueAt { get; set; }

    /// <summary>
    /// When the employee's work authorisation document(s) expire.
    /// Null for documents with no expiration (US citizen, LPR, etc.).
    /// </summary>
    public DateTimeOffset? I9ReverificationDueAt { get; set; }

    public ComplianceFormTemplate Template { get; set; } = null!;
    public FormDefinitionVersion? FormDefinitionVersion { get; set; }
    public FileAttachment? SignedPdfFile { get; set; }
    public FileAttachment? FilledPdfFile { get; set; }
}
