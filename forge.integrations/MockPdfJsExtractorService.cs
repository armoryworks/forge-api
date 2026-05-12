using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Mock pdf.js extractor for development/testing. Returns canned extraction data.
/// </summary>
public class MockPdfJsExtractorService(ILogger<MockPdfJsExtractorService> logger) : IPdfJsExtractorService
{
    public Task<PdfExtractionResult> ExtractRawAsync(byte[] pdfBytes, CancellationToken ct)
    {
        logger.LogInformation("[Mock] pdf.js extraction from {Size} bytes", pdfBytes.Length);

        var result = new PdfExtractionResult(1,
        [
            new PdfPageExtraction(
                PageNumber: 1,
                Width: 612,
                Height: 792,
                TextItems:
                [
                    new PdfTextItem("Personal Information", 45, 100, 180, 14, "Helvetica-Bold", 14, true, null),
                    new PdfTextItem("First Name", 45, 130, 60, 10, "Helvetica", 10, false, null),
                    new PdfTextItem("Last Name", 250, 130, 60, 10, "Helvetica", 10, false, null),
                    new PdfTextItem("Social Security Number", 450, 130, 120, 10, "Helvetica", 10, false, null),
                    new PdfTextItem("Signature", 45, 600, 60, 14, "Helvetica-Bold", 14, true, null),
                ],
                Annotations:
                [
                    new PdfAnnotationItem("firstName", "text", 45, 140, 180, 20, "First Name", "First Name", null, 50, true, false, null, null),
                    new PdfAnnotationItem("lastName", "text", 250, 140, 180, 20, "Last Name", "Last Name", null, 50, true, false, null, null),
                    new PdfAnnotationItem("ssn", "text", 450, 140, 120, 20, "SSN", "Social Security Number", null, 11, true, false, null, null),
                    new PdfAnnotationItem("signatureDate", "text", 45, 620, 180, 20, "Date", "Date", null, 10, true, false, null, null),
                ])
        ]);

        return Task.FromResult(result);
    }

    public Task<byte[]> RenderPageAsImageAsync(byte[] pdfBytes, int pageNumber, double scale, CancellationToken ct)
    {
        logger.LogInformation("[Mock] Rendering PDF page {Page} as image ({Size} bytes)", pageNumber, pdfBytes.Length);

        // Return a minimal 1x1 white PNG placeholder
        var pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==");
        return Task.FromResult(pngBytes);
    }
}
