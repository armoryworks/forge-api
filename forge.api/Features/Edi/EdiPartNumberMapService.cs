using System.Text;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Edi;

/// <summary>
/// ⚡ EDI BOUNDARY — per-partner part-number translation (the one remaining EDI build item,
/// docs/edi/EDI_CORE_PLAN.md §Known functional gap). Storage reuses the existing
/// <see cref="EdiMapping"/> entity (no migration): one conventional row per partner
/// (<c>TransactionSet "850"</c>, <c>Name "Part Numbers"</c>) holds the typed rows in
/// <c>ValueTranslationsJson</c>. The admin edits TYPED rows / imports CSV — never JSON.
///
/// <para>Translation is intentionally a SIMPLE number swap (partner number → our part number):
/// the 90% case. UOM / pack-quantity translation is the documented extension for when a partner
/// actually needs it.</para>
/// </summary>
public interface IEdiPartNumberMapService
{
    /// <summary>The partner's translations as a case-insensitive lookup (partner number → our number).</summary>
    Task<IReadOnlyDictionary<string, string>> GetTranslationAsync(int tradingPartnerId, CancellationToken ct = default);

    /// <summary>Typed rows for the admin editor, each resolved against the current part catalog.</summary>
    Task<IReadOnlyList<EdiPartNumberMapRow>> GetRowsAsync(int tradingPartnerId, CancellationToken ct = default);

    /// <summary>Replaces the partner's entire map with the supplied rows (the typed editor's save).</summary>
    Task<IReadOnlyList<EdiPartNumberMapRow>> ReplaceRowsAsync(
        int tradingPartnerId, IReadOnlyList<EdiPartNumberMapRow> rows, CancellationToken ct = default);

    /// <summary>Merges a CSV (PartnerPartNumber,OurPartNumber) into the map — upsert by partner number.</summary>
    Task<EdiPartNumberMapImportResultModel> ImportCsvAsync(
        int tradingPartnerId, string csv, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class EdiPartNumberMapService(AppDbContext db) : IEdiPartNumberMapService
{
    private const string PartNumberTransactionSet = "850";
    private const string PartNumberMapName = "Part Numbers";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The stored shape (only the two number strings persist).</summary>
    private sealed record StoredRow(string PartnerPartNumber, string OurPartNumber);

    public async Task<IReadOnlyDictionary<string, string>> GetTranslationAsync(
        int tradingPartnerId, CancellationToken ct = default)
    {
        var stored = await LoadStoredAsync(tradingPartnerId, ct);
        // Last-wins on duplicate partner numbers; case-insensitive (partners are inconsistent on case).
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in stored)
            if (!string.IsNullOrWhiteSpace(row.PartnerPartNumber))
                map[row.PartnerPartNumber.Trim()] = row.OurPartNumber.Trim();
        return map;
    }

    public async Task<IReadOnlyList<EdiPartNumberMapRow>> GetRowsAsync(
        int tradingPartnerId, CancellationToken ct = default)
    {
        var stored = await LoadStoredAsync(tradingPartnerId, ct);
        return await ResolveAsync(stored, ct);
    }

    public async Task<IReadOnlyList<EdiPartNumberMapRow>> ReplaceRowsAsync(
        int tradingPartnerId, IReadOnlyList<EdiPartNumberMapRow> rows, CancellationToken ct = default)
    {
        var cleaned = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.PartnerPartNumber) && !string.IsNullOrWhiteSpace(r.OurPartNumber))
            .Select(r => new StoredRow(r.PartnerPartNumber.Trim(), r.OurPartNumber.Trim()))
            .ToList();
        await SaveStoredAsync(tradingPartnerId, cleaned, ct);
        return await ResolveAsync(cleaned, ct);
    }

    public async Task<EdiPartNumberMapImportResultModel> ImportCsvAsync(
        int tradingPartnerId, string csv, CancellationToken ct = default)
    {
        var parsed = ParseCsv(csv);
        if (parsed.Count == 0)
            throw new InvalidOperationException(
                "No rows found — the CSV needs PartnerPartNumber and OurPartNumber columns (header row required).");

        var existing = (await LoadStoredAsync(tradingPartnerId, ct))
            .ToDictionary(r => r.PartnerPartNumber, r => r, StringComparer.OrdinalIgnoreCase);

        int imported = 0, updated = 0, skipped = 0;
        foreach (var (partner, ours) in parsed)
        {
            if (string.IsNullOrWhiteSpace(partner) || string.IsNullOrWhiteSpace(ours))
            {
                skipped++;
                continue;
            }
            var row = new StoredRow(partner.Trim(), ours.Trim());
            if (existing.ContainsKey(row.PartnerPartNumber))
            {
                existing[row.PartnerPartNumber] = row;
                updated++;
            }
            else
            {
                existing[row.PartnerPartNumber] = row;
                imported++;
            }
        }

        var merged = existing.Values.ToList();
        await SaveStoredAsync(tradingPartnerId, merged, ct);

        // Report how many targets don't (yet) resolve to a real part — a data-quality signal.
        var resolved = await ResolveAsync(merged, ct);
        var unresolved = resolved.Count(r => r.OurPartId is null);

        return new EdiPartNumberMapImportResultModel(imported, updated, skipped, unresolved, merged.Count);
    }

    private async Task<List<StoredRow>> LoadStoredAsync(int tradingPartnerId, CancellationToken ct)
    {
        var mapping = await db.EdiMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TradingPartnerId == tradingPartnerId
                && m.TransactionSet == PartNumberTransactionSet
                && m.Name == PartNumberMapName, ct);
        if (mapping is null || string.IsNullOrWhiteSpace(mapping.ValueTranslationsJson))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<StoredRow>>(mapping.ValueTranslationsJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task SaveStoredAsync(int tradingPartnerId, List<StoredRow> rows, CancellationToken ct)
    {
        var mapping = await db.EdiMappings
            .FirstOrDefaultAsync(m => m.TradingPartnerId == tradingPartnerId
                && m.TransactionSet == PartNumberTransactionSet
                && m.Name == PartNumberMapName, ct);

        var json = JsonSerializer.Serialize(rows, JsonOptions);
        if (mapping is null)
        {
            db.EdiMappings.Add(new EdiMapping
            {
                TradingPartnerId = tradingPartnerId,
                TransactionSet = PartNumberTransactionSet,
                Name = PartNumberMapName,
                ValueTranslationsJson = json,
                IsDefault = true,
                Notes = "Part-number translation (partner number → our part number).",
            });
        }
        else
        {
            mapping.ValueTranslationsJson = json;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Attaches OurPartId/Description by matching OurPartNumber against the catalog (one query).</summary>
    private async Task<IReadOnlyList<EdiPartNumberMapRow>> ResolveAsync(List<StoredRow> stored, CancellationToken ct)
    {
        if (stored.Count == 0)
            return [];

        var ourNumbers = stored.Select(r => r.OurPartNumber).Distinct().ToList();
        var parts = await db.Parts.AsNoTracking()
            .Where(p => ourNumbers.Contains(p.PartNumber))
            .Select(p => new { p.Id, p.PartNumber, p.Description })
            .ToListAsync(ct);
        var byNumber = parts
            .GroupBy(p => p.PartNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return stored.Select(r =>
        {
            byNumber.TryGetValue(r.OurPartNumber, out var part);
            return new EdiPartNumberMapRow
            {
                PartnerPartNumber = r.PartnerPartNumber,
                OurPartNumber = r.OurPartNumber,
                OurPartId = part?.Id,
                OurPartDescription = part?.Description,
            };
        }).ToList();
    }

    /// <summary>Two-column CSV (header required); accepts PartnerPartNumber/OurPartNumber synonyms.</summary>
    private static List<(string Partner, string Ours)> ParseCsv(string csv)
    {
        var rows = ReadCsv(csv);
        if (rows.Count < 2)
            return [];

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(params string[] names) => header.FindIndex(h => names.Any(n => h.Contains(n)));
        var partnerCol = Col("partner", "their", "customer");
        var oursCol = Col("our", "forge", "internal");
        // Fall back to positional (col 0 = partner, col 1 = ours) when headers are unrecognized.
        if (partnerCol < 0) partnerCol = 0;
        if (oursCol < 0) oursCol = 1;

        var result = new List<(string, string)>();
        foreach (var row in rows.Skip(1))
        {
            if (row.Count <= Math.Max(partnerCol, oursCol))
                continue;
            result.Add((row[partnerCol].Trim(), row[oursCol].Trim()));
        }
        return result;
    }

    private static List<List<string>> ReadCsv(string contents)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < contents.Length; i++)
        {
            var c = contents[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < contents.Length && contents[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else field.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < contents.Length && contents[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                if (row.Any(f => f.Length > 0)) rows.Add(row);
                row = [];
            }
            else field.Append(c);
        }
        row.Add(field.ToString());
        if (row.Any(f => f.Length > 0)) rows.Add(row);
        return rows;
    }
}
