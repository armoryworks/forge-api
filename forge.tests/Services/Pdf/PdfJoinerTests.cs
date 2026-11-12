using AwesomeAssertions;

using Forge.Integrations;

namespace Forge.Tests.Services.Pdf;

public sealed class PdfJoinerTests
{
    private static byte[] Pdf(int pages, string title, string author = "")
    {
        using var document = new PdfSharp.Pdf.PdfDocument();
        document.Info.Title = title;
        document.Info.Author = author;
        for (var i = 0; i < pages; i++)
            document.AddPage().Width = PdfSharp.Drawing.XUnit.FromPoint(100 + pages);

        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    [Fact]
    public void Joins_every_page_in_order_and_keeps_the_first_files_title_and_author()
    {
        var joined = PdfJoiner.Join([Pdf(2, "Count sheets PI-7", "Acme"), Pdf(3, "Second"), Pdf(1, "Third")]);

        using var document = UglyToad.PdfPig.PdfDocument.Open(joined);
        document.GetPages().Select(p => (int)Math.Round(p.Width)).Should().Equal(102, 102, 103, 103, 103, 101);
        document.Information.Title.Should().Be("Count sheets PI-7");
        document.Information.Author.Should().Be("Acme");
    }

    [Fact]
    public void One_file_is_returned_as_it_is()
    {
        var file = Pdf(1, "Only");

        PdfJoiner.Join([file]).Should().BeSameAs(file);
    }

    [Fact]
    public void Nothing_to_join_is_an_error()
    {
        var join = () => PdfJoiner.Join([]);

        join.Should().Throw<ArgumentException>();
    }
}
