using AwesomeAssertions;

using Forge.Api.Features.Inventory.PhysicalInventories;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Inventory;

public sealed class PhysicalInventoryCountSheetDocumentTests
{
    private static PhysicalInventoryDetailResponseModel Inventory(string description = "Year-end count")
        => new()
        {
            InventoryNumber = "PI-0005",
            Description = description,
            CountDate = new DateOnly(2026, 12, 31),
            ScopeLocationName = "Warehouse",
            Tags =
            [
                new PhysicalInventoryTagResponseModel { TagNumber = 1, LocationName = "Bin <b>3</b>", PartNumber = "PI-RESIN", PartDescription = "Resin", LotNumber = "LOT-1", FrozenQuantity = 12.5m, Status = PhysicalInventoryTagStatus.Open },
                new PhysicalInventoryTagResponseModel { TagNumber = 2, LocationName = "Bin 4", PartNumber = "PI-VOID", Status = PhysicalInventoryTagStatus.Void },
            ],
        };

    [Fact]
    public void Count_sheet_encodes_what_users_typed()
    {
        var html = PhysicalInventoryCountSheetDocument.Build(Inventory("<script>alert(1)</script>"), false, false, "Acme <b>Co</b>").Html;

        html.Should().NotContain("<script").And.NotContain("<b>");
        html.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;").And.Contain("Bin &lt;b&gt;3&lt;/b&gt;").And.Contain("Acme &lt;b&gt;Co&lt;/b&gt;");
    }

    [Fact]
    public void Count_sheet_footer_carries_the_inventory_number_and_skips_void_tags()
    {
        var html = PhysicalInventoryCountSheetDocument.Build(Inventory(), false, false, "Acme").Html;

        html.Should().Contain("<div class=\"page-footer\">PI-0005 &middot; Page ");
        html.Should().Contain("<body class=\"landscape\">");
        html.Should().Contain("PHYSICAL INVENTORY COUNT SHEETS").And.Contain("Count date: 12/31/2026").And.Contain("Scope: Warehouse")
            .And.Contain("Book qty").And.Contain("12.5").And.Contain("PI-RESIN").And.NotContain("PI-VOID");
        html.Split("<tr class=\"blank\">").Length.Should().Be(11);
    }

    [Fact]
    public void Blind_spanish_count_sheet_hides_book_quantities()
    {
        var html = System.Net.WebUtility.HtmlDecode(PhysicalInventoryCountSheetDocument.Build(Inventory(), true, true, null).Html);

        html.Should().Contain("HOJAS DE CONTEO DE INVENTARIO FÍSICO").And.Contain("Conteo ciego — cantidades en libros ocultas")
            .And.Contain("Hojas de conteo PI-0005").And.Contain("Ubicación");
        html.Should().NotContain("Cant. libros").And.NotContain("12.5");
        html.Should().Contain("PI-0005 · Página <span class=\"page\"></span> / <span class=\"pages\"></span></div>");
    }

    [Fact]
    public void The_company_is_the_author_of_the_file()
    {
        var html = PhysicalInventoryCountSheetDocument.Build(Inventory(), false, false, "Acme & Sons").Html;

        html.Should().Contain("<meta name=\"author\" content=\"Acme &amp; Sons\">")
            .And.Contain("PI-0005 &middot; Page <span class=\"page\"></span> / <span class=\"pages\"></span></div>");
    }

    private static PhysicalInventoryDetailResponseModel LongInventory()
        => new()
        {
            InventoryNumber = "PI-0006",
            CountDate = new DateOnly(2026, 12, 31),
            Tags = Enumerable.Range(1, 5)
                .Select(i => new PhysicalInventoryTagResponseModel
                {
                    TagNumber = i,
                    LocationName = "Bin",
                    PartNumber = $"PART-{i}",
                    Status = i == 3 ? PhysicalInventoryTagStatus.Void : PhysicalInventoryTagStatus.Open,
                })
                .ToList(),
        };

    [Fact]
    public void A_long_count_splits_into_parts_with_the_blank_rows_on_the_last()
    {
        var parts = PhysicalInventoryCountSheetDocument.BuildParts(LongInventory(), false, false, "Acme", cellsPerPart: 16);

        parts.Should().HaveCount(2);
        parts[0].Html.Should().Contain(">PART-1<").And.Contain(">PART-2<").And.NotContain(">PART-4<")
            .And.Contain("<div class=\"strong\">Part 1 of 2</div>").And.NotContain("class=\"blank\"")
            .And.Contain("<div class=\"page-footer\">PI-0006 &#183; Part 1 of 2 &middot; Page ");
        parts[1].Html.Should().Contain(">PART-4<").And.Contain(">PART-5<").And.NotContain(">PART-2<")
            .And.Contain("class=\"blank\"").And.Contain("PHYSICAL INVENTORY COUNT SHEETS");
        parts.Should().OnlyContain(p => !p.Html.Contains(">PART-3<"));
    }

    [Fact]
    public void A_split_spanish_count_names_its_parts_in_spanish()
    {
        var parts = PhysicalInventoryCountSheetDocument.BuildParts(LongInventory(), true, true, null, cellsPerPart: 14);

        System.Net.WebUtility.HtmlDecode(parts[1].Html).Should().Contain(">Parte 2 de 2<")
            .And.Contain("PI-0006 · Parte 2 de 2 · Página ");
    }

    [Fact]
    public void A_count_that_fits_is_one_part_identical_to_the_whole_document()
    {
        var part = PhysicalInventoryCountSheetDocument.BuildParts(Inventory(), false, false, "Acme").Should().ContainSingle().Subject;

        part.Html.Should().Be(PhysicalInventoryCountSheetDocument.Build(Inventory(), false, false, "Acme").Html);
        part.Html.Should().NotContain("Part 1");
    }
}
