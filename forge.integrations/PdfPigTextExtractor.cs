using System.Text;

using UglyToad.PdfPig;

using Forge.Core.Interfaces;

namespace Forge.Integrations;

/// <summary>
/// Extracts PDF text with PdfPig (Apache-2.0), fully managed and in-process.
/// </summary>
public class PdfPigTextExtractor : IPdfTextExtractor
{
    public string ExtractText(byte[] pdfBytes, int maxChars)
    {
        using var document = PdfDocument.Open(pdfBytes);
        var text = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            var pageText = string.Join(' ', page.GetWords().Select(w => w.Text));
            if (string.IsNullOrWhiteSpace(pageText))
                continue;

            if (text.Length > 0)
                text.Append("\n\n");
            text.Append(pageText);

            if (text.Length >= maxChars)
                break;
        }

        return text.Length > maxChars ? text.ToString(0, maxChars) : text.ToString();
    }
}
