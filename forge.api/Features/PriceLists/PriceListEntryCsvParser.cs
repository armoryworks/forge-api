using System.Globalization;

using CsvHelper;
using CsvHelper.Configuration;

namespace Forge.Api.Features.PriceLists;

/// <summary>
/// Single-purpose CSV parser for the PriceListEntry bulk-import flow. Headers
/// are case-insensitive and order-insensitive. Required columns:
/// <c>partNumber</c>, <c>unitPrice</c>. Optional: <c>minQuantity</c> (default
/// <c>1</c>), <c>currency</c> (caller fills the install base when blank), and
/// <c>notes</c>.
/// </summary>
internal static class PriceListEntryCsvParser
{
    /// <summary>
    /// Parse a CSV stream into a list of raw rows. Each row carries the
    /// 1-based line number (matching the file layout, header row excluded) so
    /// preview / apply errors can point the user at the exact line. Returns
    /// rows even when individual cells are malformed; cell-level validation
    /// happens in the handler so we can surface errors in the preview table.
    /// </summary>
    public static List<RawRow> Parse(Stream csvStream)
    {
        // Use CsvHelper rather than a hand-rolled split so quoted notes with
        // commas inside parse correctly. PrepareHeaderForMatch is what makes
        // headers case-insensitive.
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant(),
            MissingFieldFound = null,
            BadDataFound = null,
            HeaderValidated = null,
        };

        using var reader = new StreamReader(csvStream);
        using var csv = new CsvReader(reader, config);

        if (!csv.Read())
            return [];
        csv.ReadHeader();

        var rows = new List<RawRow>();
        // Line numbering: header is line 1, first data row is line 2.
        var lineNumber = 1;
        while (csv.Read())
        {
            lineNumber++;
            rows.Add(new RawRow(
                LineNumber: lineNumber,
                PartNumber: TryGet(csv, "partnumber"),
                UnitPriceRaw: TryGet(csv, "unitprice"),
                MinQuantityRaw: TryGet(csv, "minquantity"),
                CurrencyRaw: TryGet(csv, "currency"),
                Notes: TryGet(csv, "notes")));
        }
        return rows;
    }

    private static string? TryGet(CsvReader csv, string field)
    {
        return csv.TryGetField<string>(field, out var value) ? value?.Trim() : null;
    }

    /// <summary>Raw, untyped row fresh out of the CSV (pre-validation).</summary>
    public record RawRow(
        int LineNumber,
        string? PartNumber,
        string? UnitPriceRaw,
        string? MinQuantityRaw,
        string? CurrencyRaw,
        string? Notes);
}
