using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

public class BarcodeService(AppDbContext db, IHttpContextAccessor httpContextAccessor) : IBarcodeService
{
    private static readonly Dictionary<BarcodeEntityType, string> Prefixes = new()
    {
        [BarcodeEntityType.User] = "EMP",
        [BarcodeEntityType.Part] = "PRT",
        [BarcodeEntityType.Job] = "JOB",
        [BarcodeEntityType.SalesOrder] = "SO",
        [BarcodeEntityType.PurchaseOrder] = "PO",
        [BarcodeEntityType.Asset] = "AST",
        [BarcodeEntityType.StorageLocation] = "LOC",
        [BarcodeEntityType.Lot] = "LOT",
    };

    public async Task<Barcode> CreateBarcodeAsync(
        BarcodeEntityType entityType, int entityId, string naturalIdentifier,
        CancellationToken cancellationToken = default)
    {
        var prefix = Prefixes[entityType];
        // Some natural identifiers already carry the type prefix (lot numbers
        // are generated as LOT-yyyyMMdd-nnn) — don't stutter it into LOT-LOT-….
        var value = naturalIdentifier.StartsWith($"{prefix}-", StringComparison.Ordinal)
            ? naturalIdentifier
            : $"{prefix}-{naturalIdentifier}";
        var identityType = BarcodeIdentityType.Internal;

        // A Part carrying a licensed GS1 GTIN uses the GTIN itself as its (globally-unique) barcode value.
        if (entityType == BarcodeEntityType.Part)
        {
            var gtin = await db.Parts.Where(p => p.Id == entityId).Select(p => p.Gtin).FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(gtin))
            {
                value = gtin;
                identityType = BarcodeIdentityType.Gs1;
            }
        }

        // Internal codes get a uniqueness suffix on collision; a licensed GTIN stays exact — its
        // global uniqueness is guaranteed upstream by the parts.gtin unique index.
        if (identityType == BarcodeIdentityType.Internal)
        {
            var exists = await db.Barcodes.AnyAsync(b => b.Value == value, cancellationToken);
            if (exists)
                value = $"{value}-{entityId}";
        }

        var barcode = new Barcode
        {
            Value = value,
            EntityType = entityType,
            IsActive = true,
            IdentityType = identityType,
        };

        // Set the appropriate FK
        switch (entityType)
        {
            case BarcodeEntityType.User:
                barcode.UserId = entityId;
                break;
            case BarcodeEntityType.Part:
                barcode.PartId = entityId;
                break;
            case BarcodeEntityType.Job:
                barcode.JobId = entityId;
                break;
            case BarcodeEntityType.SalesOrder:
                barcode.SalesOrderId = entityId;
                break;
            case BarcodeEntityType.PurchaseOrder:
                barcode.PurchaseOrderId = entityId;
                break;
            case BarcodeEntityType.Asset:
                barcode.AssetId = entityId;
                break;
            case BarcodeEntityType.StorageLocation:
                barcode.StorageLocationId = entityId;
                break;
            case BarcodeEntityType.Lot:
                barcode.LotRecordId = entityId;
                break;
        }

        db.Barcodes.Add(barcode);

        // Log barcode generation on the parent entity's activity history
        var parentEntityType = entityType switch
        {
            BarcodeEntityType.User => "ApplicationUser",
            BarcodeEntityType.Part => "Part",
            BarcodeEntityType.Job => "Job",
            BarcodeEntityType.SalesOrder => "SalesOrder",
            BarcodeEntityType.PurchaseOrder => "PurchaseOrder",
            BarcodeEntityType.Asset => "Asset",
            BarcodeEntityType.StorageLocation => "StorageLocation",
            BarcodeEntityType.Lot => "Lot",
            _ => null,
        };

        if (parentEntityType != null)
        {
            var userIdClaim = httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int? userId = int.TryParse(userIdClaim, out var uid) ? uid : null;

            db.ActivityLogs.Add(new ActivityLog
            {
                EntityType = parentEntityType,
                EntityId = entityId,
                UserId = userId,
                Action = "BarcodeGenerated",
                Description = $"Barcode generated: {value}",
                FieldName = "Barcode",
                NewValue = value,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return barcode;
    }

    public async Task<Barcode?> FindByValueAsync(string value, CancellationToken cancellationToken = default)
    {
        return await db.Barcodes
            .FirstOrDefaultAsync(b => b.Value == value && b.IsActive, cancellationToken);
    }

    public async Task RefreshPartBarcodeAsync(int partId, CancellationToken cancellationToken = default)
    {
        var part = await db.Parts
            .Where(p => p.Id == partId)
            .Select(p => new { p.PartNumber, p.Gtin })
            .FirstOrDefaultAsync(cancellationToken);
        if (part is null) return;

        string value;
        BarcodeIdentityType identity;
        if (!string.IsNullOrWhiteSpace(part.Gtin))
        {
            value = part.Gtin;
            identity = BarcodeIdentityType.Gs1;
        }
        else
        {
            value = $"{Prefixes[BarcodeEntityType.Part]}-{part.PartNumber}";
            identity = BarcodeIdentityType.Internal;
        }

        var barcode = await db.Barcodes.FirstOrDefaultAsync(b => b.PartId == partId && b.IsActive, cancellationToken);
        if (barcode is null)
        {
            if (identity == BarcodeIdentityType.Internal
                && await db.Barcodes.AnyAsync(b => b.Value == value, cancellationToken))
                value = $"{value}-{partId}";
            db.Barcodes.Add(new Barcode
            {
                Value = value,
                EntityType = BarcodeEntityType.Part,
                PartId = partId,
                IsActive = true,
                IdentityType = identity,
            });
        }
        else
        {
            barcode.Value = value;
            barcode.IdentityType = identity;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
