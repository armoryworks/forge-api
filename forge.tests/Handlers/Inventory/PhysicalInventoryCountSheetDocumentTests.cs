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
}
