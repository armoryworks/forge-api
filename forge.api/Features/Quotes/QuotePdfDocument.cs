using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.Quotes;

/// <summary>
/// S3 — the PDF attached to the quote email. Cloned from
/// <see cref="Invoices.InvoicePdfDocument"/> (header / customer / lines /
/// totals) plus a terms-and-conditions appendix rendering the compiled
/// sections. Terms bodies are rendered as plain paragraphs (no markdown
/// package is referenced in this solution — see TermsCompilationService).
/// </summary>
public class QuotePdfDocument : IDocument
{
    private readonly Quote _quote;
    private readonly string _companyName;
    private readonly IReadOnlyList<CompiledTermsSection> _termsSections;

    public QuotePdfDocument(Quote quote, string companyName, IReadOnlyList<CompiledTermsSection> termsSections)
    {
        _quote = quote;
        _companyName = companyName;
        _termsSections = termsSections;
    }

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.MarginHorizontal(50);
            page.MarginVertical(40);
            page.DefaultTextStyle(x => x.FontSize(10));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(_companyName).Bold().FontSize(18);
                });

                row.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().Text("QUOTE").Bold().FontSize(24).FontColor(Colors.Blue.Darken2);
                    right.Item().Text($"#{_quote.QuoteNumber ?? _quote.Id.ToString()}").FontSize(12);
                });
            });

            col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

            col.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("Prepared For:").SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
                    left.Item().Text(_quote.Customer.CompanyName ?? _quote.Customer.Name).SemiBold();
                    if (!string.IsNullOrEmpty(_quote.Customer.Email))
                        left.Item().Text(_quote.Customer.Email).FontSize(9);
                    if (!string.IsNullOrEmpty(_quote.Customer.Phone))
                        left.Item().Text(_quote.Customer.Phone).FontSize(9);
                });

                row.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().Text($"Quote Date: {_quote.CreatedAt:MM/dd/yyyy}");
                    if (_quote.ExpirationDate.HasValue)
                        right.Item().Text($"Valid Until: {_quote.ExpirationDate:MM/dd/yyyy}");
                    if (!string.IsNullOrEmpty(_quote.CustomerPO))
                        right.Item().Text($"Customer PO: {_quote.CustomerPO}");
                });
            });

            col.Item().PaddingTop(10);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(40);    // #
                    cols.RelativeColumn(3);     // Description
                    cols.ConstantColumn(80);    // Part #
                    cols.ConstantColumn(60);    // Qty
                    cols.ConstantColumn(80);    // Unit Price
                    cols.ConstantColumn(90);    // Total
                });

                table.Header(header =>
                {
                    var headerStyle = TextStyle.Default.SemiBold().FontSize(9).FontColor(Colors.White);

                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("#").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Description").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Part #").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).AlignRight().Text("Qty").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).AlignRight().Text("Unit Price").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).AlignRight().Text("Total").Style(headerStyle);
                });

                var lines = _quote.Lines.OrderBy(l => l.LineNumber).ToList();
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                    table.Cell().Background(bg).Padding(5).Text(line.LineNumber.ToString());
                    table.Cell().Background(bg).Padding(5).Text(line.Description);
                    table.Cell().Background(bg).Padding(5).Text(line.Part?.PartNumber ?? "—");
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(line.Quantity.ToString("0.####"));
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(line.UnitPrice.ToString("C"));
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(line.LineTotal.ToString("C"));
                }
            });

            col.Item().PaddingTop(10).AlignRight().Width(200).Column(totals =>
            {
                totals.Item().Row(row =>
                {
                    row.RelativeItem().Text("Subtotal:").SemiBold();
                    row.ConstantItem(100).AlignRight().Text(_quote.Subtotal.ToString("C"));
                });

                if (_quote.TaxRate > 0)
                {
                    totals.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"Tax ({_quote.TaxRate:P0}):").SemiBold();
                        row.ConstantItem(100).AlignRight().Text(_quote.TaxAmount.ToString("C"));
                    });
                }

                totals.Item().PaddingTop(4).LineHorizontal(1);

                totals.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text("Total:").Bold().FontSize(12);
                    row.ConstantItem(100).AlignRight().Text(_quote.Total.ToString("C")).Bold().FontSize(12);
                });
            });

            if (!string.IsNullOrWhiteSpace(_quote.Notes))
            {
                col.Item().PaddingTop(20).Column(notes =>
                {
                    notes.Item().Text("Notes").SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
                    notes.Item().PaddingTop(4).Text(_quote.Notes);
                });
            }

            if (_termsSections.Count > 0)
                col.Item().PaddingTop(20).Element(ComposeTermsAppendix);
        });
    }

    private void ComposeTermsAppendix(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            col.Item().PaddingTop(8).Text("Terms & Conditions")
                .Bold().FontSize(13).FontColor(Colors.Blue.Darken2);

            foreach (var section in _termsSections)
            {
                col.Item().PaddingTop(10).Text(section.Title).SemiBold().FontSize(10);
                foreach (var paragraph in SplitParagraphs(section.BodyMarkdown))
                    col.Item().PaddingTop(3).Text(paragraph).FontSize(8.5f).FontColor(Colors.Grey.Darken3);
            }
        });
    }

    private static IEnumerable<string> SplitParagraphs(string markdown) =>
        markdown.Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void ComposeFooter(IContainer container)
    {
        container.AlignCenter().Text(text =>
        {
            text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Medium));
            text.Span("Page ");
            text.CurrentPageNumber();
            text.Span(" of ");
            text.TotalPages();
        });
    }
}
