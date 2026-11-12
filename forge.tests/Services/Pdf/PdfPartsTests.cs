using AwesomeAssertions;

using Forge.Api.Services.Pdf;

namespace Forge.Tests.Services.Pdf;

public sealed class PdfPartsTests
{
    [Fact]
    public void A_table_that_fits_is_one_part()
    {
        var rows = Enumerable.Range(1, 10).ToList();

        PdfParts.Split(rows, 10).Should().ContainSingle().Which.Should().Equal(rows);
    }

    [Fact]
    public void A_longer_table_splits_in_order_with_the_remainder_last()
    {
        var parts = PdfParts.Split(Enumerable.Range(1, 25).ToList(), 10);

        parts.Select(p => p.Count).Should().Equal(10, 10, 5);
        parts.SelectMany(p => p).Should().Equal(Enumerable.Range(1, 25));
    }

    [Fact]
    public void Rows_per_part_follow_the_column_count_and_never_reach_zero()
    {
        PdfParts.RowsPerPart(8).Should().Be(1500);
        PdfParts.RowsPerPart(15).Should().Be(800);
        PdfParts.RowsPerPart(0).Should().Be(PdfParts.CellsPerPart);
        PdfParts.RowsPerPart(8, 3).Should().Be(1);
    }

    [Fact]
    public void Only_a_split_document_names_its_part()
    {
        PdfParts.Label("PI-7", 1, 1).Should().Be("PI-7");
        PdfParts.Label("PI-7", 2, 3).Should().Be("PI-7 · Part 2 of 3");
        PdfParts.Label("PI-7", 2, 3, "Parte", "de").Should().Be("PI-7 · Parte 2 de 3");
    }
}
