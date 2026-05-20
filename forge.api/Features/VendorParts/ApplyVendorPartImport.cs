using MediatR;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorParts;

/// <summary>
/// Apply (commit) a previously-previewed VendorPart CSV bulk import. Re-parses
/// the file (we don't trust client-provided JSON for safety), then upserts each
/// row by <c>(vendorId, partId)</c>: existing rows are updated; new rows are
/// inserted; errored rows are skipped (per-row errors flow into the response).
/// Best-effort batch — one bad row doesn't abort the rest.
///
/// IsApproved / IsPreferred / IsManufacturer / Currency are NOT carried by the
/// import: new rows default to approved, non-preferred, non-manufacturer, USD;
/// updates leave those fields untouched (they're managed on the part's Sources
/// tab and the vendor-part edit dialog).
/// </summary>
public record ApplyVendorPartImportCommand(
    int VendorId,
    IFormFile File) : IRequest<VendorPartImportResultResponseModel>;

public class ApplyVendorPartImportHandler(AppDbContext db)
    : IRequestHandler<ApplyVendorPartImportCommand, VendorPartImportResultResponseModel>
{
    public async Task<VendorPartImportResultResponseModel> Handle(
        ApplyVendorPartImportCommand request, CancellationToken ct)
    {
        if (request.File is null || request.File.Length == 0)
            throw new InvalidOperationException("CSV file is required");

        if (!await db.Vendors.AnyAsync(v => v.Id == request.VendorId, ct))
            throw new KeyNotFoundException($"Vendor {request.VendorId} not found");

        await using var stream = request.File.OpenReadStream();
        var rawRows = VendorPartCsvParser.Parse(stream);

        var classified = await VendorPartImportClassifier.ClassifyAsync(db, request.VendorId, rawRows, ct);

        // Pre-load the tracked VendorParts we intend to update so the per-row
        // loop is O(1) — avoids the per-row roundtrip flagged in CLAUDE.md.
        var updatePartIds = classified
            .Where(r => r.Action == BulkImportRowAction.Update && r.PartId.HasValue)
            .Select(r => r.PartId!.Value)
            .Distinct()
            .ToList();

        var updateTargets = new Dictionary<int, VendorPart>();
        if (updatePartIds.Count > 0)
        {
            var candidates = await db.VendorParts
                .Where(vp => vp.VendorId == request.VendorId && updatePartIds.Contains(vp.PartId))
                .ToListAsync(ct);
            foreach (var vp in candidates)
                updateTargets[vp.PartId] = vp;
        }

        var addedRows = new List<(int LineNumber, int PartId, string? PartName, VendorPart Entity)>();
        var updatedParts = new List<(int PartId, string? PartName)>();
        var results = new List<VendorPartImportRowResult>(classified.Count);

        foreach (var row in classified)
        {
            switch (row.Action)
            {
                case BulkImportRowAction.Error:
                    results.Add(new VendorPartImportRowResult(
                        row.LineNumber, BulkImportRowAction.Error, null, row.ErrorMessage));
                    break;

                case BulkImportRowAction.Skip:
                    results.Add(new VendorPartImportRowResult(
                        row.LineNumber, BulkImportRowAction.Skip, null, null));
                    break;

                case BulkImportRowAction.Update:
                    if (row.PartId is int pidU && updateTargets.TryGetValue(pidU, out var existing))
                    {
                        ApplyFields(existing, row);
                        updatedParts.Add((pidU, row.PartName));
                        results.Add(new VendorPartImportRowResult(
                            row.LineNumber, BulkImportRowAction.Update, existing.Id, null));
                    }
                    else
                    {
                        results.Add(new VendorPartImportRowResult(
                            row.LineNumber, BulkImportRowAction.Error, null,
                            "Existing vendor part vanished between preview and apply"));
                    }
                    break;

                case BulkImportRowAction.Add:
                    if (row.PartId is int pidA)
                    {
                        var vp = new VendorPart
                        {
                            VendorId = request.VendorId,
                            PartId = pidA,
                            IsApproved = true,
                            IsPreferred = false,
                            IsManufacturer = false,
                            Currency = "USD",
                        };
                        ApplyFields(vp, row);
                        db.VendorParts.Add(vp);
                        addedRows.Add((row.LineNumber, pidA, row.PartName, vp));
                    }
                    else
                    {
                        results.Add(new VendorPartImportRowResult(
                            row.LineNumber, BulkImportRowAction.Error, null, "Add row missing partId"));
                    }
                    break;
            }
        }

        // Single SaveChanges for the upserts — assigns ids to the new rows.
        await db.SaveChangesAsync(ct);

        foreach (var (lineNumber, _, _, entity) in addedRows)
        {
            results.Add(new VendorPartImportRowResult(
                lineNumber, BulkImportRowAction.Add, entity.Id, null));
        }

        // Indexing-points rule: VendorPart bridges Part ↔ Vendor. We log one
        // row per affected Part (its Sources tab is where "vendor X now
        // sources this part" matters) plus a single rollup on the Vendor — a
        // per-row Vendor entry per imported part would drown the vendor's
        // Activity tab on a large catalog import.
        foreach (var (_, partId, partName, _) in addedRows)
        {
            db.LogActivityAt("vendor-source-added",
                $"Imported vendor source for {partName}", ("Part", partId));
        }
        foreach (var (partId, partName) in updatedParts)
        {
            db.LogActivityAt("vendor-source-updated",
                $"Updated vendor source for {partName} via import", ("Part", partId));
        }
        if (addedRows.Count > 0 || updatedParts.Count > 0)
        {
            db.LogActivityAt("vendor-catalog-imported",
                $"Imported parts catalog: {addedRows.Count} added, {updatedParts.Count} updated",
                ("Vendor", request.VendorId));
            await db.SaveChangesAsync(ct);
        }

        results.Sort((a, b) => a.LineNumber.CompareTo(b.LineNumber));

        return new VendorPartImportResultResponseModel(
            AddedCount: results.Count(r => r.Action == BulkImportRowAction.Add),
            UpdatedCount: results.Count(r => r.Action == BulkImportRowAction.Update),
            SkippedCount: results.Count(r => r.Action == BulkImportRowAction.Skip),
            ErrorCount: results.Count(r => r.Action == BulkImportRowAction.Error),
            Rows: results);
    }

    /// <summary>Copy the importable catalog columns from a preview row onto a VendorPart.</summary>
    private static void ApplyFields(VendorPart vp, VendorPartImportRowPreview row)
    {
        vp.VendorPartNumber = string.IsNullOrWhiteSpace(row.VendorPartNumber) ? null : row.VendorPartNumber.Trim();
        vp.ManufacturerName = string.IsNullOrWhiteSpace(row.ManufacturerName) ? null : row.ManufacturerName.Trim();
        vp.VendorMpn = string.IsNullOrWhiteSpace(row.VendorMpn) ? null : row.VendorMpn.Trim();
        vp.LeadTimeDays = row.LeadTimeDays;
        vp.MinOrderQty = row.MinOrderQty;
        vp.PackSize = row.PackSize;
        vp.CountryOfOrigin = string.IsNullOrWhiteSpace(row.CountryOfOrigin) ? null : row.CountryOfOrigin.Trim().ToUpperInvariant();
        vp.HtsCode = string.IsNullOrWhiteSpace(row.HtsCode) ? null : row.HtsCode.Trim();
        vp.Notes = string.IsNullOrWhiteSpace(row.Notes) ? null : row.Notes.Trim();
    }
}
