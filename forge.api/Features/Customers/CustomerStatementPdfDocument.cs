using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Forge.Core.Entities;

namespace Forge.Api.Features.Customers;

public class CustomerStatementPdfDocument(
    Customer customer,
    List<Invoice> invoices,
    List<Payment> payments,
    string companyName,
    DateTimeOffset statementDate) : IDocument
{
    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.MarginHorizontal(50);
            page.MarginVertical(40);
            page.DefaultTextStyle(x => x.FontSize(10));

            page.Header().Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text(companyName).FontSize(18).Bold();
                    row.ConstantItem(200).AlignRight().Column(right =>
                    {
                        right.Item().Text("CUSTOMER STATEMENT").FontSize(14).Bold();
                        right.Item().Text($"Date: {statementDate:MMM dd, yyyy}").FontSize(9);
                    });
                });

                col.Item().PaddingTop(10).Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Item().Text("Bill To:").Bold().FontSize(9);
                        left.Item().Text(customer.Name).Bold();
                        if (!string.IsNullOrWhiteSpace(customer.Email))
                            left.Item().Text(customer.Email).FontSize(9);
                        if (!string.IsNullOrWhiteSpace(customer.Phone))
                            left.Item().Text(customer.Phone).FontSize(9);
                    });
                });

                col.Item().PaddingTop(10).LineHorizontal(1);
            });

            page.Content().PaddingTop(10).Column(content =>
            {
                content.Item().Text("Invoices").FontSize(12).Bold();
                content.Item().PaddingTop(5).Table(table =>
                {
                    // WU-15 / RPT-CUSTSTMT-001: column widths previously summed
                    // to 520pt fixed plus a relative column on a Letter page
                    // with 50pt horizontal margins (~512pt content area).
                    // QuestPDF threw DocumentLayoutException ("conflicting size
                    // constraints") for every customer with invoices. Tighten
                    // the fixed columns so the table fits with the relative
                    // Status column intact.
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(75);   // Invoice #
                        cols.ConstantColumn(70);   // Date
                        cols.ConstantColumn(70);   // Due Date
                        cols.RelativeColumn();      // Status
                        cols.ConstantColumn(65);   // Total
                        cols.ConstantColumn(65);   // Paid
                        cols.ConstantColumn(65);   // Balance
                    });

                    table.Header(header =>
                    {
                        header.Cell().Background("#f0f0f0").Padding(4).Text("Invoice #").Bold().FontSize(9);
                        header.Cell().Background("#f0f0f0").Padding(4).Text("Date").Bold().FontSize(9);
                        header.Cell().Background("#f0f0f0").Padding(4).Text("Due Date").Bold().FontSize(9);
                        header.Cell().Background("#f0f0f0").Padding(4).Text("Status").Bold().FontSize(9);
                        header.Cell().Background("#f0f0f0").Padding(4).AlignRight().Text("Total").Bold().FontSize(9);
                        header.Cell().Background("#f0f0f0").Padding(4).AlignRight().Text("Paid").Bold().FontSize(9);
                        header.Cell().Background("#f0f0f0").Padding(4).AlignRight().Text("Balance").Bold().FontSize(9);
                    });

                    foreach (var inv in invoices.OrderByDescending(i => i.InvoiceDate))
                    {
                        table.Cell().Padding(4).Text(inv.InvoiceNumber).FontSize(9);
                        table.Cell().Padding(4).Text(inv.InvoiceDate.ToString("MM/dd/yyyy")).FontSize(9);
                        table.Cell().Padding(4).Text(inv.DueDate.ToString("MM/dd/yyyy")).FontSize(9);
                        table.Cell().Padding(4).Text(inv.Status.ToString()).FontSize(9);
                        table.Cell().Padding(4).AlignRight().Text(inv.Total.ToString("C")).FontSize(9);
                        table.Cell().Padding(4).AlignRight().Text(inv.AmountPaid.ToString("C")).FontSize(9);
                        table.Cell().Padding(4).AlignRight().Text(inv.BalanceDue.ToString("C")).FontSize(9);
                    }
                });

                if (payments.Count > 0)
                {
                    content.Item().PaddingTop(15).Text("Payment History").FontSize(12).Bold();
                    content.Item().PaddingTop(5).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(100);
                            cols.ConstantColumn(90);
                            cols.RelativeColumn();
                            cols.ConstantColumn(100);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background("#f0f0f0").Padding(4).Text("Payment #").Bold().FontSize(9);
                            header.Cell().Background("#f0f0f0").Padding(4).Text("Date").Bold().FontSize(9);
                            header.Cell().Background("#f0f0f0").Padding(4).Text("Method").Bold().FontSize(9);
                            header.Cell().Background("#f0f0f0").Padding(4).AlignRight().Text("Amount").Bold().FontSize(9);
                        });

                        foreach (var pmt in payments.OrderByDescending(p => p.PaymentDate))
                        {
                            table.Cell().Padding(4).Text(pmt.PaymentNumber).FontSize(9);
                            table.Cell().Padding(4).Text(pmt.PaymentDate.ToString("MM/dd/yyyy")).FontSize(9);
                            table.Cell().Padding(4).Text(pmt.Method.ToString()).FontSize(9);
                            table.Cell().Padding(4).AlignRight().Text(pmt.Amount.ToString("C")).FontSize(9);
                        }
                    });
                }

                var totalDue = invoices.Sum(i => i.BalanceDue);
                content.Item().PaddingTop(20).AlignRight().Row(row =>
                {
                    row.ConstantItem(140).Text("Total Balance Due:").Bold().FontSize(12);
                    row.ConstantItem(100).AlignRight().Text(totalDue.ToString("C")).Bold().FontSize(14);
                });
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span($"Statement generated {statementDate:MMMM dd, yyyy}").FontSize(8);
                text.Span(" | Page ").FontSize(8);
                text.CurrentPageNumber().FontSize(8);
                text.Span(" of ").FontSize(8);
                text.TotalPages().FontSize(8);
            });
        });
    }
}
