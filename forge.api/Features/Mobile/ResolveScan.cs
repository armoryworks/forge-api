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
    public async Task<ScanResolveResponseModel> Handle(ResolveScanQuery request, CancellationToken ct)
    {
        var code = request.Code.Trim();

        var barcode = await barcodes.FindByValueAsync(code, ct);
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

        var upper = code.ToUpperInvariant();
        if (upper.StartsWith("JOB-", StringComparison.Ordinal))
        {
            var id = await db.Jobs.Where(j => j.JobNumber == code).Select(j => (int?)j.Id).FirstOrDefaultAsync(ct);
            if (id is not null) return await JobAsync(id.Value, code, ct);
        }
        if (upper.StartsWith("PRT-", StringComparison.Ordinal))
        {
            var natural = code[4..];
            var id = await db.Parts.Where(p => p.PartNumber == natural).Select(p => (int?)p.Id).FirstOrDefaultAsync(ct);
            if (id is not null) return await PartAsync(id.Value, code, ct);
        }
        if (upper.StartsWith("LOT-", StringComparison.Ordinal))
        {
            var id = await db.LotRecords.Where(l => l.LotNumber == code).Select(l => (int?)l.Id).FirstOrDefaultAsync(ct);
            if (id is not null) return await LotAsync(id.Value, code, ct);
        }
        if (upper.StartsWith("EMP-", StringComparison.Ordinal))
        {
            var id = await db.Users.Where(u => u.EmployeeBarcode == code).Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
            if (id is not null) return await BadgeAsync(id.Value, code, ct);
        }

        return Unknown(code);
    }

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
