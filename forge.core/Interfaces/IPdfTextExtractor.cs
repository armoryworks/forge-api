namespace Forge.Core.Interfaces;

/// <summary>
/// Reads the plain text of a PDF in-process, for search and AI indexing.
/// </summary>
public interface IPdfTextExtractor
{
    /// <summary>
    /// Extract the words of every page, pages separated by a blank line.
    /// </summary>
    /// <param name="pdfBytes">The PDF file bytes.</param>
    /// <param name="maxChars">Stop once this many characters have been collected.</param>
    /// <returns>The extracted text, or an empty string when the PDF has no text layer.</returns>
    string ExtractText(byte[] pdfBytes, int maxChars);
}
