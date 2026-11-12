using System.Text;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Helpers;

/// <summary>
/// Stands in for the real printer: keeps every document it is asked to print, parts included, and returns
/// bytes that start with the PDF signature and carry the HTML, so a test can read what would be printed.
/// </summary>
public sealed class FakeHtmlPdfRenderer : IHtmlPdfRenderer
{
    public List<HtmlPdfDocument> Documents { get; } = [];

    public Task<byte[]> RenderAsync(HtmlPdfDocument document, CancellationToken ct)
    {
        Documents.Add(document);
        return Task.FromResult(Encoding.UTF8.GetBytes("%PDF-1.7\n" + document.Html));
    }

    public Task<byte[]> RenderAsync(IReadOnlyList<HtmlPdfDocument> parts, CancellationToken ct)
    {
        Documents.AddRange(parts);
        return Task.FromResult(Encoding.UTF8.GetBytes("%PDF-1.7\n" + string.Join("\n", parts.Select(p => p.Html))));
    }
}
