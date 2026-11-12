using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// Prints an HTML document to PDF. The document is rendered offline: scripts do not run and nothing is
/// fetched, so it can use only inline styles and images and the fonts installed on the host.
/// </summary>
public interface IHtmlPdfRenderer
{
    /// <summary>
    /// Render the document to PDF bytes. Page size, margins and the page footer come from the document's own CSS.
    /// </summary>
    /// <param name="document">The HTML to print</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The PDF file</returns>
    Task<byte[]> RenderAsync(HtmlPdfDocument document, CancellationToken ct);

    /// <summary>
    /// Render a document that was split into parts because it is too long to print in one piece. Each part
    /// prints as its own document; the result is one PDF file with the parts in order.
    /// </summary>
    /// <param name="parts">The parts to print, in order</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The PDF file</returns>
    Task<byte[]> RenderAsync(IReadOnlyList<HtmlPdfDocument> parts, CancellationToken ct);
}
