namespace Forge.Api.Services.Pdf;

/// <summary>
/// The words around the page numbers in a printed document's footer, spacing included: "Page " and " of "
/// print "Page 1 of 3".
/// </summary>
public sealed record PdfPageWords(string BeforePage, string BeforeTotal)
{
    public static PdfPageWords English { get; } = new("Page ", " of ");
}
