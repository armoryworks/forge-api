using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig;

using Forge.Api.Features.Invoices;
using Forge.Api.Features.Reports;
using Forge.Api.Services.Pdf;
using Forge.Core.Entities;
using Forge.Core.Models;
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

    [Fact]
    public async Task A_document_in_parts_prints_every_part_and_joins_them_in_order()
    {
        if (!Installed)
            return;

        var rows = Enumerable.Range(1, 130)
            .Select(i => new Dictionary<string, object?> { ["Name"] = $"row-{i:000}", ["Qty"] = i })
            .ToList();
        var parts = ReportPdfDocument.BuildParts(
            new RunReportResponseModel(["Name", "Qty"], rows, 130, null), "Open jobs", DateTimeOffset.UnixEpoch, cellsPerPart: 100);
        parts.Should().HaveCount(3);

        using var renderer = new WeasyPrintPdfRenderer(NullLogger<WeasyPrintPdfRenderer>.Instance);
        var pdf = await renderer.RenderAsync(parts, CancellationToken.None);

        var pages = PageTexts(pdf);
        pages.Count.Should().BeGreaterThanOrEqualTo(3);
        pages[0].Should().Contain("Part1of3:records1to50").And.Contain("row-001").And.Contain("Openjobs·Part1of3·Page1of");
        pages[^1].Should().Contain("row-130").And.Contain("Openjobs·Part3of3·Page");
        pages.Should().ContainSingle(page => page.Contains("Part2of3:records51to100"))
            .And.ContainSingle(page => page.Contains("Part3of3:records101to130"));
        var text = string.Concat(pages);
        foreach (var i in Enumerable.Range(1, 130))
            Regex.Matches(text, $"row-{i:000}").Should().ContainSingle();
        text.IndexOf("row-050", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("row-051", StringComparison.Ordinal));
        text.IndexOf("row-100", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("row-101", StringComparison.Ordinal));

        using var document = PdfDocument.Open(pdf);
        document.Information.Title.Should().Be("Open jobs");
    }
}
