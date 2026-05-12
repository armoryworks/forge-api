using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IDocumentSigningService
{
    Task<bool> IsAvailableAsync(CancellationToken ct);
    Task<int> CreateTemplateFromPdfAsync(string name, byte[] pdfBytes, CancellationToken ct);
    Task<DocumentSigningSubmission> CreateSubmissionAsync(int templateId, string signerEmail, string signerName, CancellationToken ct);

    /// <summary>
    /// Upload a raw PDF (no pre-existing DocuSeal template) and create a submission
    /// with one or more ordered submitters. Used for government form PDFs (W-4, I-9, state)
    /// where the filled PDF is submitted directly, bypassing template management.
    /// </summary>
    /// <param name="templateName">Name to register the one-time template under.</param>
    /// <param name="pdfBytes">The pre-filled PDF bytes to send for signing.</param>
    /// <param name="submitters">Ordered list of signers (order=1 first, etc.).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Per-submitter embed URLs keyed by order (1-based).
    /// The first submitter's URL should be sent to the employee immediately.
    /// DocuSeal sequentially unlocks subsequent submitters when preceding ones sign.
    /// </returns>
    Task<DocumentSigningMultiSubmission> CreateSubmissionFromPdfAsync(
        string templateName,
        byte[] pdfBytes,
        IReadOnlyList<SequentialSubmitter> submitters,
        CancellationToken ct);

    Task<byte[]> GetSignedPdfAsync(int submissionId, CancellationToken ct);
    Task<DocumentSigningSubmissionStatus> GetSubmissionStatusAsync(int submissionId, CancellationToken ct);
    Task DeleteTemplateAsync(int templateId, CancellationToken ct);
}
