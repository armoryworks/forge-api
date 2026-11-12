using System.Globalization;

using Forge.Api.Services.Pdf;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory.PhysicalInventories;

public static class PhysicalInventoryCountSheetDocument
{
    private const int BlankRows = 10;

    private static readonly CultureInfo Format = CultureInfo.InvariantCulture;

    private const string Styles = """
        @page { margin: 28pt 30pt 44pt; }
        body { font-size: 9pt; color: #000; }
        .sheet-head { display: flex; justify-content: space-between; align-items: flex-start; }
        .sheet-head .company { font-size: 13pt; font-weight: 700; }
        .sheet-head .title { font-size: 15pt; font-weight: 700; }
        .sheet-head .meta { width: 240pt; text-align: right; }
        .sheet-head .number { font-size: 13pt; font-weight: 700; }
        .sheet-rule { border: 0; border-top: 1.2pt solid #000; margin: 6pt 0; }
        table.count { width: 100%; border-collapse: collapse; table-layout: fixed; }
        table.count thead { display: table-header-group; }
        table.count tr { break-inside: avoid; }
        table.count th { border-bottom: 1pt solid #000; padding: 3pt 2pt; font-weight: 600; text-align: left; }
        table.count td { border-bottom: 0.5pt solid #9e9e9e; padding: 4pt 2pt; vertical-align: top; overflow-wrap: break-word; }
        table.count .tag { font-weight: 700; }
        table.count .num { text-align: right; }
        table.count tr.blank td { height: 26pt; }
        .instructions { margin-top: 10pt; font-size: 8pt; font-style: italic; }
        """;

    public static HtmlPdfDocument Build(
        PhysicalInventoryDetailResponseModel inventory,
        bool hideBookQuantity,
        bool spanish,
        string? companyName)
        => BuildParts(inventory, hideBookQuantity, spanish, companyName, int.MaxValue)[0];

    public static IReadOnlyList<HtmlPdfDocument> BuildParts(
        PhysicalInventoryDetailResponseModel inventory,
        bool hideBookQuantity,
        bool spanish,
        string? companyName,
        int cellsPerPart = PdfParts.CellsPerPart)
    {
        string L(string english, string spanishText) => spanish ? spanishText : english;

        var tags = inventory.Tags.Where(t => t.Status != PhysicalInventoryTagStatus.Void).ToList();
        var parts = PdfParts.Split(tags, PdfParts.RowsPerPart(hideBookQuantity ? 7 : 8, cellsPerPart));

        return parts.Select((part, i) =>
        {
            var partLabel = parts.Count == 1
                ? Html.Empty
                : Html.Of($"<div class=\"strong\">{L("Part", "Parte")} {i + 1} {L("of", "de")} {parts.Count}</div>");
            var body = Html.Of($"""
                {Header(inventory, hideBookQuantity, companyName, partLabel, L)}
                {Table(part, hideBookQuantity, i == parts.Count - 1, L)}
                <div class="instructions">{L(
                    "Count every item at each location. Write found items that have no tag on the blank rows; they are added as new tags.",
                    "Cuente cada artículo en cada ubicación. Anote en las filas en blanco los artículos encontrados sin etiqueta; se agregan como etiquetas nuevas.")}</div>
                """);

            return PdfHtml.Document(
                $"{L("Count sheets", "Hojas de conteo")} {inventory.InventoryNumber}",
                body,
                PdfParts.Label(inventory.InventoryNumber, i + 1, parts.Count, L("Part", "Parte"), L("of", "de")),
                landscape: true,
                styles: Styles,
                pageWords: new PdfPageWords($"{L("Page", "Página")} ", " / "),
                author: companyName);
        }).ToList();
    }

    private static Html Header(
        PhysicalInventoryDetailResponseModel inventory, bool hideBookQuantity, string? companyName, Html partLabel,
        Func<string, string, string> text)
        => Html.Of($"""
            <div class="sheet-head">
              <div>
                {(string.IsNullOrWhiteSpace(companyName) ? Html.Empty : Html.Of($"<div class=\"company\">{companyName}</div>"))}
                <div class="title">{text("PHYSICAL INVENTORY COUNT SHEETS", "HOJAS DE CONTEO DE INVENTARIO FÍSICO")}</div>
                {(string.IsNullOrWhiteSpace(inventory.Description) ? Html.Empty : Html.Of($"<div>{inventory.Description}</div>"))}
              </div>
              <div class="meta">
                <div class="number">{inventory.InventoryNumber}</div>
                <div>{text("Count date", "Fecha de conteo")}: {inventory.CountDate.ToString("MM/dd/yyyy", Format)}</div>
                <div>{text("Scope", "Alcance")}: {inventory.ScopeLocationName ?? text("All locations", "Todas las ubicaciones")}</div>
                {(hideBookQuantity ? Html.Of($"<div class=\"strong\">{text("Blind count — book quantities hidden", "Conteo ciego — cantidades en libros ocultas")}</div>") : Html.Empty)}
                {partLabel}
              </div>
            </div>
            <hr class="sheet-rule">
            """);

    private static Html Table(
        IReadOnlyList<PhysicalInventoryTagResponseModel> tags, bool hideBookQuantity, bool withBlankRows,
        Func<string, string, string> text)
    {
        var columns = hideBookQuantity
            ? Html.Of($"<col style=\"width:40pt\"><col style=\"width:131pt\"><col style=\"width:131pt\"><col style=\"width:198pt\"><col style=\"width:92pt\"><col style=\"width:80pt\"><col style=\"width:60pt\">")
            : Html.Of($"<col style=\"width:40pt\"><col style=\"width:117pt\"><col style=\"width:117pt\"><col style=\"width:176pt\"><col style=\"width:82pt\"><col style=\"width:60pt\"><col style=\"width:80pt\"><col style=\"width:60pt\">");

        var bookHeading = hideBookQuantity ? Html.Empty : Html.Of($"<th>{text("Book qty", "Cant. libros")}</th>");

        var rows = tags
            .Select(tag => Html.Of($"""
                <tr>
                  <td class="tag">{tag.TagNumber.ToString(Format)}</td>
                  <td>{tag.LocationName}</td>
                  <td>{tag.PartNumber}</td>
                  <td>{tag.PartDescription ?? string.Empty}</td>
                  <td>{tag.SerialNumber ?? tag.LotNumber ?? string.Empty}</td>
                  {(hideBookQuantity ? Html.Empty : Html.Of($"<td class=\"num\">{(tag.FrozenQuantity ?? 0m).ToString("0.####", Format)}</td>"))}
                  <td></td>
                  <td></td>
                </tr>
                """));

        var blankCells = Html.Join(Enumerable.Repeat(Html.Of($"<td></td>"), hideBookQuantity ? 7 : 8));
        var blankRows = Enumerable.Repeat(Html.Of($"<tr class=\"blank\">{blankCells}</tr>"), withBlankRows ? BlankRows : 0);

        return Html.Of($"""
            <table class="count">
              <colgroup>{columns}</colgroup>
              <thead><tr>
                <th>{text("Tag", "Etiqueta")}</th>
                <th>{text("Location", "Ubicación")}</th>
                <th>{text("Part", "Pieza")}</th>
                <th>{text("Description", "Descripción")}</th>
                <th>{text("Lot / serial", "Lote / serie")}</th>
                {bookHeading}
                <th>{text("Counted qty", "Cant. contada")}</th>
                <th>{text("Initials", "Iniciales")}</th>
              </tr></thead>
              <tbody>{rows}{blankRows}</tbody>
            </table>
            """);
    }
}
