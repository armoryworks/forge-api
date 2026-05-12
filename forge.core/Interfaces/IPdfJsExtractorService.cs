using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// Extracts raw text and form field data from PDFs using pdf.js via headless Chromium.
/// Returns structured data that the IFormDefinitionParser converts to ComplianceFormDefinition JSON.
/// </summary>
public interface IPdfJsExtractorService
{
    /// <summary>
    /// Extract text items and form field annotations from PDF bytes.
    /// </summary>
    Task<PdfExtractionResult> ExtractRawAsync(byte[] pdfBytes, CancellationToken ct);

    /// <summary>
    /// Render a PDF page as a PNG image for visual verification.
    /// </summary>
    /// <param name="pdfBytes">PDF file bytes</param>
    /// <param name="pageNumber">1-based page number</param>
    /// <param name="scale">Render scale (2.0 = high quality)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>PNG image bytes</returns>
    Task<byte[]> RenderPageAsImageAsync(byte[] pdfBytes, int pageNumber, double scale, CancellationToken ct);
}
