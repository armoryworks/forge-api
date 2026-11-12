namespace Forge.Core.Models;

/// <summary>
/// A document to print as PDF: complete HTML whose styles, page size, page footer and images are all inline.
/// </summary>
public sealed record HtmlPdfDocument(string Html);
