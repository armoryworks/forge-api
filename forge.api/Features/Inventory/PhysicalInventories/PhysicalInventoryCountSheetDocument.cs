using System.Globalization;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Forge.Api.Services;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory.PhysicalInventories;

public class PhysicalInventoryCountSheetDocument(
    PhysicalInventoryDetailResponseModel inventory,
    bool hideBookQuantity,
    bool spanish,
    string? companyName) : IDocument
{
    private const int BlankRows = 10;

    private static readonly CultureInfo Format = CultureInfo.InvariantCulture;

    private string L(string english, string spanishText) => spanish ? spanishText : english;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"{L("Count sheets", "Hojas de conteo")} {inventory.InventoryNumber}",
        Author = companyName ?? string.Empty,
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.Letter.Landscape());
            page.MarginHorizontal(30);
            page.MarginVertical(28);
            page.DefaultTextStyle(x => x.WithScriptFallbacks().FontSize(9).FontColor(Colors.Black));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().AlignCenter().Text(text =>
            {
                text.Span($"{inventory.InventoryNumber} · {L("Page", "Página")} ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
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
                    if (!string.IsNullOrWhiteSpace(companyName))
                        left.Item().Text(companyName).Bold().FontSize(13);
                    left.Item().Text(L("PHYSICAL INVENTORY COUNT SHEETS", "HOJAS DE CONTEO DE INVENTARIO FÍSICO")).Bold().FontSize(15);
                    if (!string.IsNullOrWhiteSpace(inventory.Description))
                        left.Item().Text(inventory.Description);
                });
                row.ConstantItem(240).AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Text(inventory.InventoryNumber).Bold().FontSize(13);
                    right.Item().AlignRight().Text($"{L("Count date", "Fecha de conteo")}: {inventory.CountDate.ToString("MM/dd/yyyy", Format)}");
                    right.Item().AlignRight().Text(
                        $"{L("Scope", "Alcance")}: {inventory.ScopeLocationName ?? L("All locations", "Todas las ubicaciones")}");
                    if (hideBookQuantity)
                        right.Item().AlignRight().Text(L("Blind count — book quantities hidden", "Conteo ciego — cantidades en libros ocultas")).SemiBold();
                });
            });
            col.Item().PaddingTop(6).LineHorizontal(1.2f).LineColor(Colors.Black);
            col.Item().PaddingTop(6);
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
                    cols.ConstantColumn(40);
                    cols.RelativeColumn(2);
                    cols.RelativeColumn(2);
                    cols.RelativeColumn(3);
                    cols.RelativeColumn(1.4f);
                    if (!hideBookQuantity)
                        cols.ConstantColumn(60);
                    cols.ConstantColumn(80);
                    cols.ConstantColumn(60);
                });

                table.Header(header =>
                {
                    HeaderCell(header.Cell(), L("Tag", "Etiqueta"));
                    HeaderCell(header.Cell(), L("Location", "Ubicación"));
                    HeaderCell(header.Cell(), L("Part", "Pieza"));
                    HeaderCell(header.Cell(), L("Description", "Descripción"));
                    HeaderCell(header.Cell(), L("Lot / serial", "Lote / serie"));
                    if (!hideBookQuantity)
                        HeaderCell(header.Cell(), L("Book qty", "Cant. libros"));
                    HeaderCell(header.Cell(), L("Counted qty", "Cant. contada"));
                    HeaderCell(header.Cell(), L("Initials", "Iniciales"));
                });

                foreach (var tag in inventory.Tags.Where(t => t.Status != PhysicalInventoryTagStatus.Void))
                {
                    BodyCell(table.Cell()).Text(tag.TagNumber.ToString(Format)).Bold();
                    BodyCell(table.Cell()).Text(tag.LocationName);
                    BodyCell(table.Cell()).Text(tag.PartNumber);
                    BodyCell(table.Cell()).Text(tag.PartDescription ?? string.Empty);
                    BodyCell(table.Cell()).Text(tag.SerialNumber ?? tag.LotNumber ?? string.Empty);
                    if (!hideBookQuantity)
                        BodyCell(table.Cell()).AlignRight().Text((tag.FrozenQuantity ?? 0m).ToString("0.####", Format));
                    BodyCell(table.Cell()).Text(string.Empty);
                    BodyCell(table.Cell()).Text(string.Empty);
                }

                for (var i = 0; i < BlankRows; i++)
                {
                    var cells = hideBookQuantity ? 7 : 8;
                    for (var c = 0; c < cells; c++)
                        BodyCell(table.Cell()).Height(18).Text(string.Empty);
                }
            });

            col.Item().PaddingTop(10).Text(L(
                "Count every item at each location. Write found items that have no tag on the blank rows; they are added as new tags.",
                "Cuente cada artículo en cada ubicación. Anote en las filas en blanco los artículos encontrados sin etiqueta; se agregan como etiquetas nuevas."))
                .FontSize(8).Italic();
        });
    }

    private static void HeaderCell(IContainer cell, string text)
        => cell.BorderBottom(1).BorderColor(Colors.Black).PaddingVertical(3).PaddingHorizontal(2).Text(text).SemiBold();

    private static IContainer BodyCell(IContainer cell)
        => cell.BorderBottom(0.5f).BorderColor(Colors.Grey.Medium).PaddingVertical(4).PaddingHorizontal(2);
}
