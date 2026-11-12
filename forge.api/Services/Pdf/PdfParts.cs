namespace Forge.Api.Services.Pdf;

/// <summary>
/// Splits a long table into parts that each print as their own document. The printer lays out about a
/// thousand table cells a second on a fast machine and a print is stopped after a minute, so a part holds
/// <see cref="CellsPerPart"/> cells, about 13 seconds of work there. A table that fits is one part and
/// prints exactly as it would unsplit. A table without fixed column widths prints at half the speed, so
/// give it half the cells.
/// </summary>
public static class PdfParts
{
    public const int CellsPerPart = 12_000;

    public static int RowsPerPart(int columns, int cellsPerPart = CellsPerPart)
        => Math.Max(1, cellsPerPart / Math.Max(1, columns));

    public static IReadOnlyList<IReadOnlyList<T>> Split<T>(IReadOnlyList<T> rows, int rowsPerPart)
        => rows.Count <= rowsPerPart ? [rows] : rows.Chunk(Math.Max(1, rowsPerPart)).ToList();

    public static string Label(string label, int part, int parts, string partWord = "Part", string ofWord = "of")
        => parts == 1 ? label : $"{label} · {partWord} {part} {ofWord} {parts}";
}
