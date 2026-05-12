using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// Verifies an extracted ComplianceFormDefinition against the raw PDF data.
/// Runs structural checks and optionally uses AI to refine the result.
/// </summary>
public interface IFormDefinitionVerifier
{
    /// <summary>
    /// Verify a form definition against the raw extraction data.
    /// If verification fails, attempts AI-assisted refinement (up to maxIterations).
    /// </summary>
    /// <param name="formDefinitionJson">The ComplianceFormDefinition JSON to verify.</param>
    /// <param name="rawResult">The raw pdf.js extraction data (ground truth).</param>
    /// <param name="formType">Form type identifier for AI context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Verification result including corrected JSON if refinement succeeded.</returns>
    Task<FormVerificationResult> VerifyAsync(
        string formDefinitionJson,
        PdfExtractionResult rawResult,
        string formType,
        CancellationToken ct);

    /// <summary>
    /// Verify a form definition with visual verification against the source PDF.
    /// Renders each page of the source PDF as an image, sends to AI vision model alongside
    /// the extracted definition, and iterates until layout matches or max iterations reached.
    /// </summary>
    /// <param name="formDefinitionJson">The ComplianceFormDefinition JSON to verify.</param>
    /// <param name="rawResult">The raw pdf.js extraction data (ground truth).</param>
    /// <param name="pdfBytes">Original PDF file bytes for visual rendering.</param>
    /// <param name="formType">Form type identifier for AI context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Verification result including visual verification status and corrected JSON.</returns>
    Task<FormVerificationResult> VerifyWithVisualAsync(
        string formDefinitionJson,
        PdfExtractionResult rawResult,
        byte[] pdfBytes,
        string formType,
        CancellationToken ct);
}
