using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig;

using Forge.Api.Features.Invoices;
using Forge.Api.Services.Pdf;
using Forge.Core.Entities;
using Forge.Integrations;

namespace Forge.Tests.Services.Pdf;

/// <summary>
/// Prints through the real WeasyPrint when the machine has it (the API image, a CI runner that installs it);
/// on a machine without it the tests return without checking anything.
/// </summary>
public sealed class WeasyPrintPdfRendererTests
{
    private static readonly bool Installed = CanImportWeasyPrint();

    private static bool CanImportWeasyPrint()
    {
        try
        {
            var start = new ProcessStartInfo(WeasyPrintPdfRenderer.Python) { RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("import weasyprint");
            using var process = Process.Start(start)!;
            process.WaitForExit(30_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static List<string> PageTexts(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return document.GetPages().Select(page => Regex.Replace(page.Text, @"\s+", string.Empty)).ToList();
    }

    [Fact]
    public async Task Prints_the_document_without_running_scripts_or_loading_anything()
    {
        if (!Installed)
            return;

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var body = new Html($"<p>Printed text</p><img src=\"http://127.0.0.1:{port}/pixel.png\"><img src=\"file:///etc/hostname\">"
            + $"<link rel=\"stylesheet\" href=\"http://127.0.0.1:{port}/a.css\"><script>document.body.innerHTML = 'SCRIPT RAN'</script>");

        using var renderer = new WeasyPrintPdfRenderer(NullLogger<WeasyPrintPdfRenderer>.Instance);
        var pdf = await renderer.RenderAsync(PdfHtml.Document("Probe", body, "DOC <1>"), CancellationToken.None);

        pdf.Should().StartWith("%PDF-"u8.ToArray());
        var page = PageTexts(pdf).Should().ContainSingle().Subject;
        page.Should().Contain("Printedtext").And.Contain("DOC<1>·Page1of1").And.NotContain("SCRIPTRAN");
        listener.Pending().Should().BeFalse("the document must not be able to make requests");
    }

    [Fact]
    public async Task A_long_invoice_runs_onto_more_pages_and_repeats_the_column_headings()
    {
        if (!Installed)
            return;

        var invoice = new Invoice
        {
            InvoiceNumber = "INV-9001",
            Customer = new Customer { Name = "Brindlecove Fluid Controls" },
            InvoiceDate = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 12, 3, 0, 0, 0, TimeSpan.Zero),
        };
        for (var i = 1; i <= 90; i++)
            invoice.Lines.Add(new InvoiceLine { LineNumber = i, Description = $"Machined bracket {i}", Quantity = 2, UnitPrice = 12.5m });

        using var renderer = new WeasyPrintPdfRenderer(NullLogger<WeasyPrintPdfRenderer>.Instance);
        var pdf = await renderer.RenderAsync(InvoicePdfDocument.Build(invoice, "Quillfeather Precision Works"), CancellationToken.None);

        var pages = PageTexts(pdf);
        pages.Count.Should().BeGreaterThan(1);
        pages[0].Should().Contain("QuillfeatherPrecisionWorks").And.Contain("INVOICE").And.Contain("BrindlecoveFluidControls");
        pages[1].Should().Contain("UnitPrice").And.Contain($"INV-9001·Page2of{pages.Count}");
        pages[^1].Should().Contain("Total:");
    }

    [Fact]
    public async Task A_landscape_document_prints_wider_than_tall()
    {
        if (!Installed)
            return;

        using var renderer = new WeasyPrintPdfRenderer(NullLogger<WeasyPrintPdfRenderer>.Instance);
        var pdf = await renderer.RenderAsync(PdfHtml.Document("Wide", new Html("<p>Wide</p>"), landscape: true), CancellationToken.None);

        using var document = PdfDocument.Open(pdf);
        var page = document.GetPage(1);
        page.Width.Should().BeGreaterThan(page.Height);
    }
}
