using Forge.Core.Models;

namespace Forge.Api.Services.Pdf;

/// <summary>
/// The page shell and stylesheet shared by printed documents: US Letter, a footer on every page with the
/// document's label and the page count, and the fonts the API image installs. Lato prints Latin text; names
/// in other scripts fall back to Noto CJK, Amiri and Noto Color Emoji. A translated document passes its own
/// <see cref="PdfPageWords"/>; a footer note prints above the page count on every page; the author is stored
/// in the file's properties.
/// </summary>
public static class PdfHtml
{
    private const string Styles = """
        @page { margin: 40pt 50pt 54pt; @bottom-center { content: element(page-footer); } }
        @page portrait { size: Letter; }
        @page landscape { size: Letter landscape; }
        * { box-sizing: border-box; }
        body {
          margin: 0;
          font-family: Lato, 'Liberation Sans', 'Noto Sans CJK JP', Amiri;
          font-size: 10pt;
          line-height: 1.3;
          color: #000;
        }
        body.portrait { page: portrait; }
        body.landscape { page: landscape; }
        .page-footer { position: running(page-footer); font-size: 8pt; color: #9e9e9e; text-align: center; }
        .page-footer .page::after { content: counter(page); }
        .page-footer .pages::after { content: counter(pages); }
        .page-footer .footer-note { margin-bottom: 2pt; }
        .head, .parties { display: flex; justify-content: space-between; align-items: flex-start; }
        .head .company { font-size: 18pt; font-weight: 700; padding-right: 20pt; }
        .head .title { font-size: 24pt; font-weight: 700; color: #1976d2; line-height: 1.1; }
        .head .number { font-size: 12pt; }
        .rule { border: 0; border-top: 1pt solid #bdbdbd; margin: 10pt 0; }
        .parties { margin-bottom: 14pt; }
        .right { text-align: right; }
        .label { font-size: 9pt; font-weight: 600; color: #757575; }
        .small { font-size: 9pt; }
        .strong { font-weight: 600; }
        table.lines { width: 100%; border-collapse: collapse; }
        table.lines thead { display: table-header-group; }
        table.lines thead tr { background: #1976d2; }
        table.lines th { color: #fff; font-size: 9pt; font-weight: 600; padding: 5pt; text-align: left; }
        table.lines td { padding: 5pt; vertical-align: top; }
        table.lines tbody tr:nth-child(even) { background: #f5f5f5; }
        table.lines tr { break-inside: avoid; }
        table.lines .num { text-align: right; white-space: nowrap; }
        .totals { width: 220pt; margin: 10pt 0 0 auto; break-inside: avoid; }
        .totals .row { display: flex; justify-content: space-between; padding: 1pt 0; }
        .totals .row span:last-child { white-space: nowrap; padding-left: 10pt; }
        .totals .strong-label span:first-child { font-weight: 600; }
        .totals .grand { font-size: 12pt; font-weight: 700; }
        .totals .after-rule { border-top: 1pt solid #000; margin-top: 4pt; padding-top: 4pt; }
        .totals .credit { color: #388e3c; }
        .totals .note { font-size: 8pt; color: #757575; }
        .notes { margin-top: 20pt; break-inside: avoid; }
        .notes .body { margin-top: 4pt; white-space: pre-wrap; }
        """;

    public static HtmlPdfDocument Document(
        string title,
        Html body,
        string? footerLabel = null,
        bool landscape = false,
        string? styles = null,
        PdfPageWords? pageWords = null,
        Html? footerNote = null,
        string? author = null)
    {
        var words = pageWords ?? PdfPageWords.English;
        var label = string.IsNullOrWhiteSpace(footerLabel) ? Html.Empty : Html.Of($"{footerLabel} &middot; ");
        var note = footerNote is { } text ? Html.Of($"<div class=\"footer-note\">{text}</div>") : Html.Empty;
        var authorMeta = string.IsNullOrWhiteSpace(author) ? Html.Empty : Html.Of($"<meta name=\"author\" content=\"{author}\">");
        var html = Html.Of($"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><title>{title}</title>{authorMeta}<style>{new Html(Styles)}{new Html(styles ?? string.Empty)}</style></head>
            <body class="{(landscape ? "landscape" : "portrait")}">
            <div class="page-footer">{note}{label}{words.BeforePage}<span class="page"></span>{words.BeforeTotal}<span class="pages"></span></div>
            {body}
            </body>
            </html>
            """);
        return new HtmlPdfDocument(html.Value);
    }
}
