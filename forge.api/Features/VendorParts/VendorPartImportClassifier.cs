using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorParts;

/// <summary>
/// Validates and classifies parsed CSV rows for the VendorPart import flow.
/// Pre-loads the lookup data the row-by-row pass needs (parts by part number,
/// existing VendorParts for the target vendor) so we avoid the N+1 trap
/// flagged in CLAUDE.md. Classification is per <c>(vendorId, partId)</c>:
/// an existing row → Update, otherwise → Add. A part referenced twice in the
/// same file errors on the second occurrence (the batch upsert can't add the
/// same pair twice).
/// </summary>
internal static class VendorPartImportClassifier
{
    private sealed record PartLookupRow(int Id, string PartNumber, string Name);

    public static async Task<List<VendorPartImportRowPreview>> ClassifyAsync(
        AppDbContext db,
        int vendorId,
        IReadOnlyList<VendorPartCsvParser.RawRow> rawRows,
        CancellationToken ct)
    {
        var distinctPartNumbers = rawRows
            .Where(r => !string.IsNullOrWhiteSpace(r.PartNumber))
            .Select(r => r.PartNumber!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var partsByNumber = await db.Parts
            .AsNoTracking()
            .Where(p => distinctPartNumbers.Contains(p.PartNumber))
            .Select(p => new PartLookupRow(p.Id, p.PartNumber, p.Name))
            .ToListAsync(ct);

        var partLookup = partsByNumber
            .GroupBy(p => p.PartNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var existingPartIds = (await db.VendorParts
            .AsNoTracking()
            .Where(vp => vp.VendorId == vendorId)
            .Select(vp => vp.PartId)
            .ToListAsync(ct))
            .ToHashSet();

        var seenPartIds = new HashSet<int>();
        var output = new List<VendorPartImportRowPreview>(rawRows.Count);
        foreach (var raw in rawRows)
        {
            output.Add(ClassifyOne(raw, partLookup, existingPartIds, seenPartIds));
        }
        return output;
    }

    private static VendorPartImportRowPreview ClassifyOne(
        VendorPartCsvParser.RawRow raw,
        IReadOnlyDictionary<string, PartLookupRow> partLookup,
        IReadOnlySet<int> existingPartIds,
        HashSet<int> seenPartIds)
    {
        VendorPartImportRowPreview Error(string message, int? partId = null, string? partName = null) =>
            new(raw.LineNumber, raw.PartNumber, partName, partId,
                raw.VendorPartNumber, raw.ManufacturerName, raw.VendorMpn,
                null, null, null, raw.CountryOfOrigin, raw.HtsCode, raw.Notes,
                BulkImportRowAction.Error, message);

        // 1. Required: partNumber.
        if (string.IsNullOrWhiteSpace(raw.PartNumber))
            return Error("partNumber is required");

        // 2. String length caps mirror CreateVendorPartValidator.
        if (raw.VendorPartNumber is { Length: > 100 })
            return Error("vendorPartNumber must be <= 100 chars");
        if (raw.ManufacturerName is { Length: > 200 })
            return Error("manufacturerName must be <= 200 chars");
        if (raw.VendorMpn is { Length: > 100 })
            return Error("vendorMpn must be <= 100 chars");
        if (raw.CountryOfOrigin is { Length: > 2 })
            return Error("countryOfOrigin must be a 2-letter code");
        if (raw.HtsCode is { Length: > 20 })
            return Error("htsCode must be <= 20 chars");
        if (raw.Notes is { Length: > 2000 })
            return Error("notes must be <= 2000 chars");

        // 3. Optional non-negative integers.
        if (!TryParseNonNegativeInt(raw.LeadTimeDaysRaw, out var leadTimeDays))
            return Error("leadTimeDays must be a whole number >= 0");
        if (!TryParseNonNegativeInt(raw.MinOrderQtyRaw, out var minOrderQty))
            return Error("minOrderQty must be a whole number >= 0");
        if (!TryParseNonNegativeInt(raw.PackSizeRaw, out var packSize))
            return Error("packSize must be a whole number >= 0");

        // 4. Resolve the part.
        if (!partLookup.TryGetValue(raw.PartNumber.Trim(), out var part))
            return Error($"Part '{raw.PartNumber}' not found");

        // 5. In-file duplicate guard — a (vendor, part) pair can't be added
        // twice in one batch.
        if (!seenPartIds.Add(part.Id))
            return Error($"Part '{raw.PartNumber}' appears more than once in the file", part.Id, part.Name);

        var action = existingPartIds.Contains(part.Id)
            ? BulkImportRowAction.Update
            : BulkImportRowAction.Add;

        return new VendorPartImportRowPreview(
            raw.LineNumber, raw.PartNumber, part.Name, part.Id,
            raw.VendorPartNumber, raw.ManufacturerName, raw.VendorMpn,
            leadTimeDays, minOrderQty, packSize,
            raw.CountryOfOrigin, raw.HtsCode, raw.Notes,
            action, ErrorMessage: null);
    }

    /// <summary>
    /// Blank → (true, null). A parseable non-negative integer → (true, value).
    /// Anything else → (false, null). Decimals are rejected (these are counts
    /// / day-counts, not fractional).
    /// </summary>
    private static bool TryParseNonNegativeInt(string? raw, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw))
            return true;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
            return false;
        value = parsed;
        return true;
    }
}
