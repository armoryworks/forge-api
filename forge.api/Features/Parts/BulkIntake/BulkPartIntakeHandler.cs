using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Parts.BulkIntake;

/// <summary>
/// Part bulk-intake. One handler powers preview (dry-run) and commit; the only difference is
/// whether Created rows are inserted, barcoded, and activity-logged. Per row:
///   1. Name required, and name / description / external id within their column lengths (Invalid otherwise).
///   2. Within-batch dedup by name / external id (DuplicateWithinBatch).
///   3. Existing-part dedup by name OR external id (DuplicateExistingPart).
///   4. Otherwise Created.
/// Mirrors the customer/lead bulk-intake pattern. ProcurementSource / InventoryClass are parsed
/// leniently from free text (Buy / Component fallbacks). Part numbers are server-issued: the
/// generator is seeded once per inventory class then incremented locally so a single batch of
/// same-class parts gets sequential numbers without a round-trip per row. Preview returns the
/// numbers the rows would get; commit issues them.
/// </summary>
public record BulkPartIntakeCommand(BulkPartIntakeRequest Request, bool Commit)
    : IRequest<BulkPartIntakeResponseModel>;

public class BulkPartIntakeHandler(
    AppDbContext db, IPartRepository repo, IBarcodeService barcodeService, IBusinessIdentifierService identifiers,
    IClock clock)
    : IRequestHandler<BulkPartIntakeCommand, BulkPartIntakeResponseModel>
{
    public async Task<BulkPartIntakeResponseModel> Handle(BulkPartIntakeCommand request, CancellationToken ct)
    {
        var rows = request.Request.Rows ?? [];
        if (rows.Count == 0)
            return new BulkPartIntakeResponseModel(0, 0, 0, []);
        if (rows.Count > 1000)
            throw new InvalidOperationException("Bulk intake is capped at 1000 rows per upload.");

        var names = rows.Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => r.Name.Trim().ToLowerInvariant()).Distinct().ToList();
        var externalIds = rows.Where(r => !string.IsNullOrWhiteSpace(r.ExternalId))
            .Select(r => r.ExternalId!.Trim().ToLowerInvariant()).Distinct().ToList();

        var existingByName = (await db.Parts.AsNoTracking()
                .Where(p => names.Contains(p.Name.ToLower()))
                .Select(p => new { p.Id, p.Name })
                .ToListAsync(ct))
            .GroupBy(p => p.Name.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Id);

        var existingByExternalId = (await db.Parts.AsNoTracking()
                .Where(p => p.ExternalId != null && externalIds.Contains(p.ExternalId.ToLower()))
                .Select(p => new { p.Id, p.ExternalId })
                .ToListAsync(ct))
            .Where(p => p.ExternalId != null)
            .GroupBy(p => p.ExternalId!.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Id);

        var results = new List<BulkPartIntakeRowResult>(rows.Count);
        var seenNames = new HashSet<string>();
        var seenExternalIds = new HashSet<string>();
        var toCreate = new List<Part>();

        foreach (var row in rows)
        {
            var key = row.ExternalRowKey;

            if (string.IsNullOrWhiteSpace(row.Name))
            {
                results.Add(new BulkPartIntakeRowResult(key, BulkPartIntakeRowStatus.Invalid, null, null, null, "Name is required"));
                continue;
            }

            var lengthProblem = LengthProblem(row);
            if (lengthProblem is not null)
            {
                results.Add(new BulkPartIntakeRowResult(key, BulkPartIntakeRowStatus.Invalid, null, null, null, lengthProblem));
                continue;
            }

            var nameNorm = row.Name.Trim().ToLowerInvariant();
            var extNorm = string.IsNullOrWhiteSpace(row.ExternalId) ? null : row.ExternalId.Trim().ToLowerInvariant();

            if (seenNames.Contains(nameNorm) || (extNorm is not null && seenExternalIds.Contains(extNorm)))
            {
                results.Add(new BulkPartIntakeRowResult(key, BulkPartIntakeRowStatus.DuplicateWithinBatch, null, null, null, "Duplicate name / external id earlier in batch"));
                continue;
            }

            if (existingByName.TryGetValue(nameNorm, out var matchByName))
            {
                results.Add(new BulkPartIntakeRowResult(key, BulkPartIntakeRowStatus.DuplicateExistingPart, null, null, matchByName, "Name matches an existing part"));
                continue;
            }
            if (extNorm is not null && existingByExternalId.TryGetValue(extNorm, out var matchByExt))
            {
                results.Add(new BulkPartIntakeRowResult(key, BulkPartIntakeRowStatus.DuplicateExistingPart, null, null, matchByExt, "External id matches an existing part"));
                continue;
            }

            seenNames.Add(nameNorm);
            if (extNorm is not null) seenExternalIds.Add(extNorm);

            toCreate.Add(new Part
            {
                Name = row.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(row.Description) ? null : row.Description.Trim(),
                Revision = "A",
                Status = PartStatus.Draft,
                ProcurementSource = ParseProcurementSource(row.ProcurementSource),
                InventoryClass = ParseInventoryClass(row.InventoryClass),
                ExternalId = string.IsNullOrWhiteSpace(row.ExternalId) ? null : row.ExternalId.Trim(),
            });
            results.Add(new BulkPartIntakeRowResult(key, BulkPartIntakeRowStatus.Created, null, null, null, null));
        }

        if (toCreate.Count > 0)
            await AssignPartNumbersAsync(toCreate, ct);

        if (request.Commit && toCreate.Count > 0)
        {
            db.Parts.AddRange(toCreate);
            await db.SaveChangesAsync(ct);

            var effectiveDate = clock.UtcNow;
            foreach (var part in toCreate)
            {
                PartRevisionBaseline.Add(db, part, effectiveDate, "Initial release");
                db.LogActivityAt("bulk-intake-created", $"Created part via bulk import: {part.PartNumber} — {part.Name}", ("Part", part.Id));
            }
            await db.SaveChangesAsync(ct);

            // Barcodes: CreateBarcodeAsync owns the uniqueness + GTIN-vs-internal decision and
            // saves per call, so it runs per created part (batch is capped at 1000).
            foreach (var part in toCreate)
            {
                await barcodeService.CreateBarcodeAsync(BarcodeEntityType.Part, part.Id, part.PartNumber, ct);
                await identifiers.IssueAsync(BusinessEntityType.Part, part.Id, part.PartNumber, ct);
            }
        }

        using (var created = toCreate.GetEnumerator())
        {
            for (var i = 0; i < results.Count; i++)
            {
                if (results[i].Status == BulkPartIntakeRowStatus.Created && created.MoveNext())
                    results[i] = results[i] with
                    {
                        CreatedPartId = request.Commit ? created.Current.Id : null,
                        CreatedPartNumber = created.Current.PartNumber,
                    };
            }
        }

        var createdCount = results.Count(r => r.Status == BulkPartIntakeRowStatus.Created);
        return new BulkPartIntakeResponseModel(rows.Count, createdCount, results.Count - createdCount, results);
    }

    /// <summary>Seeds the sequential generator once per inventory class (extracting the prefix +
    /// starting number from the repo's next-number string) then increments locally so a batch of
    /// same-class parts gets contiguous numbers without a DB round-trip per part.</summary>
    private async Task AssignPartNumbersAsync(List<Part> parts, CancellationToken ct)
    {
        var seeds = new Dictionary<InventoryClass, (string Prefix, int Next)>();
        foreach (var part in parts)
        {
            if (!seeds.TryGetValue(part.InventoryClass, out var seed))
            {
                var next = await repo.GetNextPartNumberAsync(part.InventoryClass, ct); // e.g. "PRT-00042"
                var dash = next.LastIndexOf('-');
                var prefix = dash >= 0 ? next[..(dash + 1)] : next;
                var startNum = int.TryParse(dash >= 0 ? next[(dash + 1)..] : next, out var parsed) ? parsed : 1;
                seed = (prefix, startNum);
            }

            part.PartNumber = $"{seed.Prefix}{seed.Next:D5}";
            seeds[part.InventoryClass] = (seed.Prefix, seed.Next + 1);
        }
    }

    /// <summary>The first trimmed value too long for its column, as a row message; null when all fit.</summary>
    private static string? LengthProblem(BulkPartIntakeRow row)
    {
        if (row.Name.Trim().Length > NameMaxLength)
            return $"Name is too long (max {NameMaxLength} characters)";
        if (row.Description is not null && row.Description.Trim().Length > DescriptionMaxLength)
            return $"Description is too long (max {DescriptionMaxLength} characters)";
        if (row.ExternalId is not null && row.ExternalId.Trim().Length > ExternalIdMaxLength)
            return $"Part number / external id is too long (max {ExternalIdMaxLength} characters)";
        return null;
    }

    private const int NameMaxLength = 256;
    private const int DescriptionMaxLength = 2000;
    private const int ExternalIdMaxLength = 100;

    private static ProcurementSource ParseProcurementSource(string? raw)
    {
        var norm = Normalize(raw);
        return norm switch
        {
            "make" or "manufacture" or "manufactured" or "build" => ProcurementSource.Make,
            "buy" or "purchase" or "purchased" or "bought" => ProcurementSource.Buy,
            "subcontract" or "sub" or "outsource" or "outsourced" => ProcurementSource.Subcontract,
            "phantom" => ProcurementSource.Phantom,
            _ => ProcurementSource.Buy,
        };
    }

    private static InventoryClass ParseInventoryClass(string? raw)
    {
        var norm = Normalize(raw);
        return norm switch
        {
            "raw" or "rawmaterial" or "material" => InventoryClass.Raw,
            "component" or "comp" or "part" => InventoryClass.Component,
            "subassembly" or "assembly" or "asm" or "sub" => InventoryClass.Subassembly,
            "finishedgood" or "finished" or "fg" or "finishedgoods" => InventoryClass.FinishedGood,
            "consumable" or "con" => InventoryClass.Consumable,
            "tool" or "tooling" or "tlg" => InventoryClass.Tool,
            "service" or "svc" or "nre" => InventoryClass.Service,
            _ => InventoryClass.Component,
        };
    }

    /// <summary>Lower-cases and strips spaces / hyphens / underscores so "Finished Good",
    /// "finished-good", and "finishedgood" all collapse to one token.</summary>
    private static string Normalize(string? raw)
        => new string((raw ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
