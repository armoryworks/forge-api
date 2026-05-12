namespace Forge.Core.Models;

public record DocumentSigningSubmission(int SubmissionId, string SubmitUrl);

public record DocumentSigningSubmissionStatus(string Status, DateTimeOffset? CompletedAt);

/// <summary>
/// One signer in a sequential multi-party signing flow.
/// </summary>
/// <param name="Order">1-based signing order. Order=1 signs first; subsequent orders unlock after preceding signs.</param>
/// <param name="Email">Signer's email address.</param>
/// <param name="Name">Signer's display name.</param>
/// <param name="Role">DocuSeal role name (must match a role defined in the template or be auto-created).</param>
public record SequentialSubmitter(int Order, string Email, string Name, string Role);

/// <summary>
/// Result of CreateSubmissionFromPdfAsync — one entry per submitter, keyed by Order.
/// </summary>
/// <param name="DocuSealTemplateId">The one-time DocuSeal template ID created from the uploaded PDF.</param>
/// <param name="SubmittersByOrder">
/// Embed URLs per submitter (key = Order, value = (SubmitterId, EmbedUrl)).
/// Present the Order=1 URL to the employee immediately; subsequent URLs are provided after preceding signers complete.
/// </param>
public record DocumentSigningMultiSubmission(
    int DocuSealTemplateId,
    IReadOnlyDictionary<int, SubmitterResult> SubmittersByOrder);

/// <summary>Per-submitter result from a multi-submission creation.</summary>
public record SubmitterResult(int SubmitterId, string EmbedUrl);
