using MediatR;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.PriceLists;

/// <summary>
/// Apply (commit) a previously-previewed PriceListEntry CSV bulk import.
/// Re-parses the file (we don't trust client-provided JSON for safety), then
/// upserts each row by <c>(partId, minQuantity)</c>: existing rows are
/// updated; new rows are inserted; errored rows are skipped (per-row errors
/// flow into the response). Best-effort batch — one failure doesn't abort
/// the rest.
/// </summary>
public record ApplyPriceListEntryImportCommand(
    int PriceListId,
    IFormFile File) : IRequest<BulkImportResultResponseModel>;

public class ApplyPriceListEntryImportHandler(
    AppDbContext db,
    IPriceListRepository repo,
    ICurrencyService currency)
    : IRequestHandler<ApplyPriceListEntryImportCommand, BulkImportResultResponseModel>
{
    public async Task<BulkImportResultResponseModel> Handle(
        ApplyPriceListEntryImportCommand request, CancellationToken cancellationToken)
    {
        if (request.File is null || request.File.Length == 0)
            throw new InvalidOperationException("CSV file is required");

        if (!await repo.PriceListExistsAsync(request.PriceListId, cancellationToken))
            throw new KeyNotFoundException($"Price list {request.PriceListId} not found");

        var baseCurrency = await currency.GetBaseCurrencyAsync(cancellationToken);

        await using var stream = request.File.OpenReadStream();
        var rawRows = PriceListEntryCsvParser.Parse(stream);

        var classified = await PriceListEntryImportClassifier.ClassifyAsync(
            db, request.PriceListId, rawRows, baseCurrency, cancellationToken);

        // Pre-load existing tracked entities once per (partId, minQty) we're
        // touching. Avoids the per-row roundtrip flagged in CLAUDE.md.
        var updateKeys = classified
            .Where(r => r.Action == BulkImportRowAction.Update && r.PartId.HasValue)
            .Select(r => (PartId: r.PartId!.Value, r.MinQuantity))
            .Distinct()
            .ToList();

        var updateTargets = new Dictionary<(int PartId, int MinQuantity), PriceListEntry>();
        if (updateKeys.Count > 0)
        {
            // EF Core can't translate a tuple membership check, so build a
            // single OR-chain by loading the candidates and filtering in
            // memory. The candidate set is bounded by the price list size.
            var partIds = updateKeys.Select(k => k.PartId).Distinct().ToList();
            var candidates = await db.PriceListEntries
                .Where(e => e.PriceListId == request.PriceListId && partIds.Contains(e.PartId))
                .ToListAsync(cancellationToken);
            foreach (var e in candidates)
            {
                updateTargets[(e.PartId, e.MinQuantity)] = e;
            }
        }

        // Track the DB id we want to surface back per CSV line. Adds get
        // their id assigned post-SaveChanges; we map via a placeholder.
        var addedEntries = new List<(int LineNumber, PriceListEntry Entry)>();
        var results = new List<BulkImportRowResult>(classified.Count);

        foreach (var row in classified)
        {
            switch (row.Action)
            {
                case BulkImportRowAction.Error:
                    results.Add(new BulkImportRowResult(
                        row.LineNumber, BulkImportRowAction.Error, null, row.ErrorMessage));
                    break;

                case BulkImportRowAction.Skip:
                    results.Add(new BulkImportRowResult(
                        row.LineNumber, BulkImportRowAction.Skip, null, null));
                    break;

                case BulkImportRowAction.Update:
                    if (row.PartId is int pidU
                        && updateTargets.TryGetValue((pidU, row.MinQuantity), out var existing))
                    {
                        existing.UnitPrice = row.UnitPrice ?? 0m;
                        existing.Currency = row.Currency;
                        existing.Notes = row.Notes;
                        results.Add(new BulkImportRowResult(
                            row.LineNumber, BulkImportRowAction.Update, existing.Id, null));
                    }
                    else
                    {
                        results.Add(new BulkImportRowResult(
                            row.LineNumber, BulkImportRowAction.Error, null,
                            "Existing entry vanished between preview and apply"));
                    }
                    break;

                case BulkImportRowAction.Add:
                    if (row.PartId is int pidA && row.UnitPrice is decimal price)
                    {
                        var entry = new PriceListEntry
                        {
                            PriceListId = request.PriceListId,
                            PartId = pidA,
                            UnitPrice = price,
                            MinQuantity = row.MinQuantity,
                            Currency = row.Currency,
                            Notes = row.Notes,
                        };
                        await repo.AddEntryAsync(entry, cancellationToken);
                        addedEntries.Add((row.LineNumber, entry));
                    }
                    else
                    {
                        results.Add(new BulkImportRowResult(
                            row.LineNumber, BulkImportRowAction.Error, null,
                            "Add row missing partId or unitPrice"));
                    }
                    break;
            }
        }

        // Single SaveChanges — wraps everything in one transaction. If a
        // unique-constraint races us we surface a top-level 500 (rare, no
        // partial commit).
        await repo.SaveChangesAsync(cancellationToken);

        // Now that ids are assigned, build the Add results.
        foreach (var (lineNumber, entry) in addedEntries)
        {
            results.Add(new BulkImportRowResult(
                lineNumber, BulkImportRowAction.Add, entry.Id, null));
        }

        // Sort results by line number for predictable client display.
        results.Sort((a, b) => a.LineNumber.CompareTo(b.LineNumber));

        return new BulkImportResultResponseModel(
            AddedCount: results.Count(r => r.Action == BulkImportRowAction.Add),
            UpdatedCount: results.Count(r => r.Action == BulkImportRowAction.Update),
            SkippedCount: results.Count(r => r.Action == BulkImportRowAction.Skip),
            ErrorCount: results.Count(r => r.Action == BulkImportRowAction.Error),
            Rows: results);
    }
}
