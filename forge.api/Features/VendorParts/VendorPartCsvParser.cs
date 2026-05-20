using System.Globalization;

using CsvHelper;
using CsvHelper.Configuration;

namespace Forge.Api.Features.VendorParts;

/// <summary>
/// Single-purpose CSV parser for the VendorPart bulk-import flow. Headers are
/// case-insensitive and order-insensitive. Required column: <c>partNumber</c>.
/// Optional: <c>vendorPartNumber</c>, <c>manufacturerName</c>, <c>vendorMpn</c>,
/// <c>leadTimeDays</c>, <c>minOrderQty</c>, <c>packSize</c>,
/// <c>countryOfOrigin</c>, <c>htsCode</c>, <c>notes</c>.
///
/// IsPreferred is intentionally NOT importable — preference is a per-Part
/// decision (which vendor is preferred for that part) made on the part's
/// Sources tab, not a property of a one-vendor catalog dump. Keeping it out
/// avoids the cross-Part "unset siblings" side effect during a batch upsert.
/// </summary>
internal static class VendorPartCsvParser
{
    /// <summary>
    /// Parse a CSV stream into raw rows. Each row carries the 1-based line
    /// number (header excluded) so preview / apply errors point at the exact
    /// line. Returns rows even when individual cells are malformed; cell-level
    /// validation happens in the classifier so errors surface in the preview.
    /// </summary>
    public static List<RawRow> Parse(Stream csvStream)
    {
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
                VendorPartNumber: TryGet(csv, "vendorpartnumber"),
                ManufacturerName: TryGet(csv, "manufacturername"),
                VendorMpn: TryGet(csv, "vendormpn"),
                LeadTimeDaysRaw: TryGet(csv, "leadtimedays"),
                MinOrderQtyRaw: TryGet(csv, "minorderqty"),
                PackSizeRaw: TryGet(csv, "packsize"),
                CountryOfOrigin: TryGet(csv, "countryoforigin"),
                HtsCode: TryGet(csv, "htscode"),
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
        string? VendorPartNumber,
        string? ManufacturerName,
        string? VendorMpn,
        string? LeadTimeDaysRaw,
        string? MinOrderQtyRaw,
        string? PackSizeRaw,
        string? CountryOfOrigin,
        string? HtsCode,
        string? Notes);
}
