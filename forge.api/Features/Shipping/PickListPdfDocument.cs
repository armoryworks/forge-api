using Forge.Api.Services.Pdf;
using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.Shipping;

public static class PickListPdfDocument
{
    private const string Styles = """
        .summary { margin-top: 20pt; }
        .summary .lines-count { font-size: 9pt; color: #757575; }
        .signatures { display: flex; margin-top: 40pt; break-inside: avoid; }
        .signatures .line { flex: 1; border-top: 1pt solid #9e9e9e; padding-top: 4pt; font-size: 9pt; color: #757575; }
        .signatures .line + .line { margin-left: 40pt; }
        """;

    public static HtmlPdfDocument Build(PickWave wave, string? companyName, DateTimeOffset generatedAt)
    {
        var body = Html.Of($"{Header(wave, companyName, generatedAt)}{Lines(wave)}{Summary(wave)}{Notes(wave)}{Signatures()}");
        return PdfHtml.Document($"Pick List {wave.WaveNumber}", body, wave.WaveNumber, styles: Styles);
    }

    private static Html Header(PickWave wave, string? companyName, DateTimeOffset generatedAt)
        => Html.Of($"""
            <div class="head">
              <div class="company">{companyName}</div>
              <div class="right"><div class="title">PICK LIST</div><div class="number">Wave #{wave.WaveNumber}</div></div>
            </div>
            <hr class="rule">
            <div class="parties small">
              <div>
                <div>Status: {wave.Status}</div>
                <div>Strategy: {wave.Strategy}</div>
                {(wave.ReleasedAt.HasValue ? Html.Of($"<div>Released: {wave.ReleasedAt.Value:MMM dd, yyyy HH:mm}</div>") : Html.Empty)}
              </div>
              <div class="right">
                <div>Total Lines: {wave.TotalLines}</div>
                <div>Picked: {wave.PickedLines}</div>
                <div>Date: {generatedAt:MMM dd, yyyy}</div>
              </div>
            </div>
            """);

    private static Html Lines(PickWave wave)
    {
        var rows = wave.Lines
            .OrderBy(l => l.SortOrder)
            .Select((line, i) =>
            {
                var location = !string.IsNullOrEmpty(line.BinPath) ? line.BinPath : line.FromLocation?.Name ?? "—";
                return Html.Of($"""
                    <tr>
                      <td>{i + 1}</td>
                      <td>{line.Part?.PartNumber ?? "—"}</td>
                      <td>{line.Part?.Description ?? "—"}</td>
                      <td>{location}</td>
                      <td class="num">{line.RequestedQuantity:G}</td>
                      <td class="num">{line.PickedQuantity:G}</td>
                      <td>{line.Status.ToString()}</td>
                    </tr>
                    """);
            });

        return Html.Of($"""
            <table class="lines">
              <colgroup><col style="width:35pt"><col style="width:90pt"><col style="width:124pt"><col style="width:83pt"><col style="width:60pt"><col style="width:60pt"><col style="width:60pt"></colgroup>
              <thead><tr><th>#</th><th>Part #</th><th>Description</th><th>Location</th><th class="num">Req</th><th class="num">Picked</th><th>Status</th></tr></thead>
              <tbody>{rows}</tbody>
            </table>
            """);
    }

    private static Html Summary(PickWave wave)
        => Html.Of($"""
            <div class="summary">
              <div class="strong">Total Items: {wave.Lines.Sum(l => l.RequestedQuantity):G}</div>
              <div class="lines-count">Total Lines: {wave.Lines.Count}</div>
            </div>
            """);

    private static Html Notes(PickWave wave)
        => string.IsNullOrWhiteSpace(wave.Notes)
            ? Html.Empty
            : Html.Of($"<div class=\"notes\"><div class=\"label\">Notes</div><div class=\"body\">{wave.Notes}</div></div>");

    private static Html Signatures()
        => Html.Of($"<div class=\"signatures\"><div class=\"line\">Picked By</div><div class=\"line\">Date</div></div>");
}
