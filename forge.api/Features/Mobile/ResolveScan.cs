using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Mobile;

public record ResolveScanQuery(string Code) : IRequest<ScanResolveResponseModel>;

public class ResolveScanValidator : AbstractValidator<ResolveScanQuery>
{
    public ResolveScanValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(200);
    }
}

/// <summary>
/// Turns a scanned value into an entity. Exact barcodes-table lookup first
/// (internal, GS1, manual codes and the collision suffix all live there),
/// then the natural-identifier fallback per docs labels.md. Unknown codes
/// resolve to kind "unknown" — the app buzzes, never navigates.
/// </summary>
public class ResolveScanHandler(AppDbContext db, IBarcodeService barcodes)
    : IRequestHandler<ResolveScanQuery, ScanResolveResponseModel>
{
    private const string LikeEscape = "\\";

    public async Task<ScanResolveResponseModel> Handle(ResolveScanQuery request, CancellationToken ct)
    {
        var code = request.Code.Trim();
        var upper = code.ToUpperInvariant();

        var barcode = await barcodes.FindByValueAsync(code, ct);
        if (barcode is null && upper != code)
            barcode = await barcodes.FindByValueAsync(upper, ct);
        if (barcode is not null)
        {
            return barcode.EntityType switch
            {
                BarcodeEntityType.Job => await JobAsync(barcode.JobId!.Value, code, ct),
                BarcodeEntityType.Part => await PartAsync(barcode.PartId!.Value, code, ct),
                BarcodeEntityType.StorageLocation => await BinAsync(barcode.StorageLocationId!.Value, code, ct),
                BarcodeEntityType.Lot => await LotAsync(barcode.LotRecordId!.Value, code, ct),
                BarcodeEntityType.User => await BadgeAsync(barcode.UserId!.Value, code, ct),
                BarcodeEntityType.SalesOrder => new("salesOrder", barcode.SalesOrderId, code, code, null),
                BarcodeEntityType.PurchaseOrder => new("purchaseOrder", barcode.PurchaseOrderId, code, code, null),
                BarcodeEntityType.Asset => new("asset", barcode.AssetId, code, code, null),
                _ => Unknown(code),
            };
        }

        if (upper.StartsWith("JOB-", StringComparison.Ordinal))
        {
            var id = await FindJobAsync(code, ct);
            if (id is not null) return await JobAsync(id.Value, code, ct);
        }
        if (upper.StartsWith("PRT-", StringComparison.Ordinal))
        {
            var natural = code[4..];
            var pattern = EscapeLike(natural);
            var candidates = await db.Parts.AsNoTracking()
                .Where(p => EF.Functions.ILike(p.PartNumber, pattern, LikeEscape))
                .Select(p => new { p.Id, Value = p.PartNumber })
                .Take(10)
                .ToListAsync(ct);
            var id = PickOne(candidates.Select(c => (c.Id, c.Value)).ToList(), natural);
            if (id is not null) return await PartAsync(id.Value, code, ct);
        }
        if (upper.StartsWith("LOT-", StringComparison.Ordinal))
        {
            var pattern = EscapeLike(code);
            var candidates = await db.LotRecords.AsNoTracking()
                .Where(l => EF.Functions.ILike(l.LotNumber, pattern, LikeEscape))
                .Select(l => new { l.Id, Value = l.LotNumber })
                .Take(10)
                .ToListAsync(ct);
            var id = PickOne(candidates.Select(c => (c.Id, c.Value)).ToList(), code);
            if (id is not null) return await LotAsync(id.Value, code, ct);
        }
        if (upper.StartsWith("EMP-", StringComparison.Ordinal))
        {
            var pattern = EscapeLike(code);
            var candidates = await db.Users.AsNoTracking()
                .Where(u => u.EmployeeBarcode != null && EF.Functions.ILike(u.EmployeeBarcode, pattern, LikeEscape))
                .Select(u => new { u.Id, Value = u.EmployeeBarcode! })
                .Take(10)
                .ToListAsync(ct);
            var id = PickOne(candidates.Select(c => (c.Id, c.Value)).ToList(), code);
            if (id is not null) return await BadgeAsync(id.Value, code, ct);
        }

        return Unknown(code);
    }

    private async Task<int?> FindJobAsync(string code, CancellationToken ct)
    {
        var wanted = JobKey(code, unpad: false);
        if (wanted.Length == 0) return null;

        var unpadded = JobKey(code, unpad: true);
        var digits = TrailingDigits(unpadded);
        var pattern = "%" + EscapeLike(digits.Length > 0 ? digits : unpadded);
        var candidates = await db.Jobs.AsNoTracking()
            .Where(j => EF.Functions.ILike(j.JobNumber, pattern, LikeEscape))
            .Select(j => new { j.Id, j.JobNumber })
            .ToListAsync(ct);

        var exact = candidates.Where(c => JobKey(c.JobNumber, unpad: false) == wanted).Select(c => c.Id).ToList();
        if (exact.Count > 0) return exact.Count == 1 ? exact[0] : null;

        var loose = candidates.Where(c => JobKey(c.JobNumber, unpad: true) == unpadded).Select(c => c.Id).ToList();
        return loose.Count == 1 ? loose[0] : null;
    }

    private static string JobKey(string value, bool unpad)
    {
        var key = value.Trim().ToUpperInvariant();
        if (key.StartsWith("JOB-", StringComparison.Ordinal))
            key = key[4..];
        if (!unpad) return key;

        var digits = TrailingDigits(key);
        if (digits.Length == 0) return key;
        var trimmed = digits.TrimStart('0');
        return key[..^digits.Length] + (trimmed.Length == 0 ? "0" : trimmed);
    }

    private static string TrailingDigits(string value)
    {
        var start = value.Length;
        while (start > 0 && char.IsAsciiDigit(value[start - 1])) start--;
        return value[start..];
    }

    private static int? PickOne(List<(int Id, string Value)> candidates, string wanted)
    {
        var exact = candidates.Where(c => string.Equals(c.Value, wanted, StringComparison.Ordinal)).ToList();
        if (exact.Count == 1) return exact[0].Id;
        return candidates.Count == 1 ? candidates[0].Id : null;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static ScanResolveResponseModel Unknown(string code) => new("unknown", null, code, code, null);

    private async Task<ScanResolveResponseModel> JobAsync(int id, string code, CancellationToken ct)
    {
        var job = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == id)
            .Select(j => new { j.JobNumber, j.Title, Customer = j.Customer != null ? j.Customer.Name : null })
            .FirstAsync(ct);
        return new("job", id, code, job.JobNumber, job.Customer ?? job.Title);
    }

    private async Task<ScanResolveResponseModel> PartAsync(int id, string code, CancellationToken ct)
    {
        var part = await db.Parts.AsNoTracking()
            .Where(p => p.Id == id).Select(p => new { p.PartNumber, p.Name }).FirstAsync(ct);
        return new("part", id, code, part.PartNumber, part.Name);
    }

    private async Task<ScanResolveResponseModel> BinAsync(int id, string code, CancellationToken ct)
    {
        var name = await db.StorageLocations.AsNoTracking()
            .Where(l => l.Id == id).Select(l => l.Name).FirstAsync(ct);
        return new("bin", id, code, name, null);
    }

    private async Task<ScanResolveResponseModel> LotAsync(int id, string code, CancellationToken ct)
    {
        var lot = await db.LotRecords.AsNoTracking()
            .Where(l => l.Id == id).Select(l => l.LotNumber).FirstAsync(ct);
        return new("lot", id, code, lot, null);
    }

    private async Task<ScanResolveResponseModel> BadgeAsync(int id, string code, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == id).Select(u => new { u.FirstName, u.LastName }).FirstAsync(ct);
        return new("badge", id, code, $"{user.LastName}, {user.FirstName}", null);
    }
}
