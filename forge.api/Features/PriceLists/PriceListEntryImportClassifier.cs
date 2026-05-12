using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.PriceLists;

/// <summary>
/// Validates and classifies parsed CSV rows for the PriceListEntry import flow.
/// Pre-loads the lookup data the row-by-row pass needs (parts by part number,
/// existing entries by <c>(partId, minQuantity)</c>) so we avoid the N+1
/// trap flagged in CLAUDE.md.
/// </summary>
internal static class PriceListEntryImportClassifier
{
    /// <summary>Lightweight part snapshot used during classification.</summary>
    private sealed record PartLookupRow(int Id, string PartNumber, string Name);

    /// <summary>
    /// Convert parsed CSV rows into typed preview rows. Performs lookups for
    /// part numbers + conflict detection against existing entries on the
    /// target list. Caller already validated that the price list exists.
    /// </summary>
    public static async Task<List<BulkImportRowPreview>> ClassifyAsync(
        AppDbContext db,
        int priceListId,
        IReadOnlyList<PriceListEntryCsvParser.RawRow> rawRows,
        string baseCurrency,
        CancellationToken ct)
    {
        // Pre-load lookup tables so the per-row loop is O(1) per row.
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
            .ToDictionary(
                g => g.Key,
                g => g.First(),
                StringComparer.OrdinalIgnoreCase);

        var existingEntries = await db.PriceListEntries
            .AsNoTracking()
            .Where(e => e.PriceListId == priceListId)
            .Select(e => new { e.Id, e.PartId, e.MinQuantity })
            .ToListAsync(ct);

        var existingByKey = existingEntries.ToDictionary(
            e => (e.PartId, e.MinQuantity),
            e => e.Id);

        var output = new List<BulkImportRowPreview>(rawRows.Count);
        foreach (var raw in rawRows)
        {
            output.Add(ClassifyOne(raw, partLookup, existingByKey, baseCurrency));
        }
        return output;
    }

    private static BulkImportRowPreview ClassifyOne(
        PriceListEntryCsvParser.RawRow raw,
        IReadOnlyDictionary<string, PartLookupRow> partLookup,
        IReadOnlyDictionary<(int PartId, int MinQuantity), int> existingByKey,
        string baseCurrency)
    {
        // 1. Required: partNumber.
        if (string.IsNullOrWhiteSpace(raw.PartNumber))
        {
            return new BulkImportRowPreview(
                raw.LineNumber, raw.PartNumber, null, null, null, 1,
                baseCurrency, raw.Notes,
                BulkImportRowAction.Error, "partNumber is required");
        }

        // 2. Required: unitPrice (parseable).
        if (string.IsNullOrWhiteSpace(raw.UnitPriceRaw)
            || !decimal.TryParse(raw.UnitPriceRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var unitPrice))
        {
            return new BulkImportRowPreview(
                raw.LineNumber, raw.PartNumber, null, null, null, 1,
                baseCurrency, raw.Notes,
                BulkImportRowAction.Error, "unitPrice is required and must be a number");
        }
        if (unitPrice < 0)
        {
            return new BulkImportRowPreview(
                raw.LineNumber, raw.PartNumber, null, null, unitPrice, 1,
                baseCurrency, raw.Notes,
                BulkImportRowAction.Error, "unitPrice must be >= 0");
        }

        // 3. Optional minQuantity (default 1). Entity stores int, so we
        // coerce decimals up by truncation rather than fail — matches the
        // existing form-dialog leniency.
        var minQuantity = 1;
        if (!string.IsNullOrWhiteSpace(raw.MinQuantityRaw))
        {
            if (!decimal.TryParse(raw.MinQuantityRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var mq))
            {
                return new BulkImportRowPreview(
                    raw.LineNumber, raw.PartNumber, null, null, unitPrice, 1,
                    baseCurrency, raw.Notes,
                    BulkImportRowAction.Error, "minQuantity must be a number");
            }
            if (mq < 1)
            {
                return new BulkImportRowPreview(
                    raw.LineNumber, raw.PartNumber, null, null, unitPrice, 1,
                    baseCurrency, raw.Notes,
                    BulkImportRowAction.Error, "minQuantity must be >= 1");
            }
            minQuantity = (int)mq;
        }

        // 4. Optional currency (default install base). Shape-check only —
        // we don't have a currency table to validate against.
        var currency = string.IsNullOrWhiteSpace(raw.CurrencyRaw)
            ? baseCurrency
            : raw.CurrencyRaw.Trim().ToUpperInvariant();
        if (currency.Length != 3 || !currency.All(char.IsLetter))
        {
            return new BulkImportRowPreview(
                raw.LineNumber, raw.PartNumber, null, null, unitPrice, minQuantity,
                currency, raw.Notes,
                BulkImportRowAction.Error, "currency must be a 3-letter ISO code");
        }

        // 5. Notes length cap mirrors the entity validator.
        if (raw.Notes is { Length: > 2000 })
        {
            return new BulkImportRowPreview(
                raw.LineNumber, raw.PartNumber, null, null, unitPrice, minQuantity,
                currency, raw.Notes,
                BulkImportRowAction.Error, "notes must be <= 2000 chars");
        }

        // 6. Resolve the part.
        if (!partLookup.TryGetValue(raw.PartNumber.Trim(), out var part))
        {
            return new BulkImportRowPreview(
                raw.LineNumber, raw.PartNumber, null, null, unitPrice, minQuantity,
                currency, raw.Notes,
                BulkImportRowAction.Error, $"Part '{raw.PartNumber}' not found");
        }

        // 7. Conflict check — does an entry already exist for this
        // (partId, minQty)?
        var action = existingByKey.ContainsKey((part.Id, minQuantity))
            ? BulkImportRowAction.Update
            : BulkImportRowAction.Add;

        return new BulkImportRowPreview(
            raw.LineNumber, raw.PartNumber, part.Name, part.Id, unitPrice, minQuantity,
            currency, raw.Notes,
            action, ErrorMessage: null);
    }
}
