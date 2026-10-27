using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using QuestPDF.Fluent;

using Forge.Api.Features.Files;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Quality.Coa;

/// <summary>
/// Issues certificates of analysis (CAP-QC-COA). A certificate freezes the lot's inspection and SPC results
/// (<see cref="CoaResultCollector"/>), takes the next COA-###### number, and archives the PDF as issued with
/// the lot's files. Attaching to a shipment issues one certificate per lot the shipment carried for the
/// shipment's customer; a lot whose results are out of specification, or that has none, is not attached
/// and comes back as a warning instead.
/// </summary>
public class CertificateOfAnalysisIssuer(
    AppDbContext db,
    IClock clock,
    IStorageService storage,
    IOptions<MinioOptions> minioOptions)
{
    public const string NonConformingCode = "coa-nonconforming";
    public const string NoResultsCode = "coa-no-results";
    private const string NumberPrefix = "COA-";
    private const string LotFilesEntityType = "lots";

    public async Task<CertificateOfAnalysis> IssueAsync(
        int lotId, int? customerId, int? shipmentId, string? language, string? notes, int? userId, CancellationToken ct)
    {
        var lot = await db.LotRecords.FirstOrDefaultAsync(l => l.Id == lotId, ct)
            ?? throw new KeyNotFoundException($"Lot {lotId} not found.");
        if (customerId is int cid && !await db.Customers.AnyAsync(c => c.Id == cid, ct))
            throw new KeyNotFoundException($"Customer {cid} not found.");
        if (shipmentId is int sid && !await db.Shipments.AnyAsync(s => s.Id == sid, ct))
            throw new KeyNotFoundException($"Shipment {sid} not found.");

        var results = await CoaResultCollector.CollectAsync(db, lot, ct);
        if (results.Count == 0)
            throw new InvalidOperationException(
                $"Lot {lot.LotNumber} has no completed inspection or SPC results to certify.");

        return await SaveAsync(lot, results, customerId, shipmentId, language, notes, userId, ct);
    }

    public async Task<ShipmentCertificatesResponseModel> AttachToShipmentAsync(
        int shipmentId, int? lotId, string? language, int? userId, CancellationToken ct)
    {
        var shipment = await db.Shipments.AsNoTracking()
            .Where(s => s.Id == shipmentId)
            .Select(s => new { s.Id, s.SalesOrder.CustomerId })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Shipment {shipmentId} not found.");

        var lots = lotId is int id
            ? await LotsOnShipmentPartsAsync(shipmentId, id, ct)
            : await ShippedLotsAsync(shipmentId, ct);

        var alreadyAttached = await db.CertificatesOfAnalysis.AsNoTracking()
            .Where(c => c.ShipmentId == shipmentId && c.Status == CertificateOfAnalysisStatus.Issued)
            .Select(c => c.LotRecordId)
            .ToListAsync(ct);

        var warnings = new List<CoaAttachWarningModel>();
        foreach (var lot in lots.Where(l => !alreadyAttached.Contains(l.Id)))
        {
            var results = await CoaResultCollector.CollectAsync(db, lot, ct);
            var partNumber = await db.Parts.AsNoTracking().Where(p => p.Id == lot.PartId).Select(p => p.PartNumber).FirstAsync(ct);
            if (results.Count == 0)
            {
                warnings.Add(new CoaAttachWarningModel(lot.Id, lot.LotNumber, partNumber, NoResultsCode, []));
                continue;
            }
            if (results.Any(r => !r.Pass))
            {
                warnings.Add(new CoaAttachWarningModel(lot.Id, lot.LotNumber, partNumber, NonConformingCode,
                    results.Where(r => !r.Pass).Select(r => r.Characteristic).ToList()));
                continue;
            }
            await SaveAsync(lot, results, shipment.CustomerId, shipmentId, language, null, userId, ct);
        }

        var attached = await CertificateOfAnalysisMapping.ListAsync(
            db, db.CertificatesOfAnalysis.Where(c => c.ShipmentId == shipmentId), ct);
        return new ShipmentCertificatesResponseModel(attached, warnings);
    }

    public async Task<byte[]> RenderPdfAsync(int certificateId, string? language, CancellationToken ct)
    {
        var certificate = await db.CertificatesOfAnalysis.AsNoTracking()
            .Include(c => c.LotRecord)
            .Include(c => c.Part)
            .Include(c => c.Customer)
            .Include(c => c.Shipment)
            .Include(c => c.Results)
            .FirstOrDefaultAsync(c => c.Id == certificateId, ct)
            ?? throw new KeyNotFoundException($"Certificate of analysis {certificateId} not found.");

        var resolved = await DocumentLanguage.ResolveAsync(db, language ?? certificate.Language, ct);
        return await RenderAsync(certificate, resolved, ct);
    }

    private async Task<CertificateOfAnalysis> SaveAsync(
        LotRecord lot, List<CoaResult> results, int? customerId, int? shipmentId,
        string? language, string? notes, int? userId, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        await db.AcquireAdvisoryLockAsync("certificates-of-analysis.number", string.Empty, ct);

        var resolvedLanguage = await DocumentLanguage.ResolveAsync(db, language, ct);
        var certificate = new CertificateOfAnalysis
        {
            CertificateNumber = await NextNumberAsync(ct),
            LotRecordId = lot.Id,
            PartId = lot.PartId,
            CustomerId = customerId,
            ShipmentId = shipmentId,
            Status = CertificateOfAnalysisStatus.Issued,
            Conforms = results.All(r => r.Pass),
            Language = resolvedLanguage,
            GeneratedAt = clock.UtcNow,
            GeneratedById = userId is > 0 ? userId : null,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
        foreach (var result in results)
            certificate.Results.Add(result);

        db.CertificatesOfAnalysis.Add(certificate);
        await db.SaveChangesAsync(ct);

        await db.Entry(certificate).Reference(c => c.LotRecord).LoadAsync(ct);
        await db.Entry(certificate).Reference(c => c.Part).LoadAsync(ct);
        await db.Entry(certificate).Reference(c => c.Customer).LoadAsync(ct);
        await db.Entry(certificate).Reference(c => c.Shipment).LoadAsync(ct);

        var pdf = await RenderAsync(certificate, resolvedLanguage, ct);
        var bucket = FileEntityTypes.ResolveBucket(LotFilesEntityType, minioOptions.Value);
        var objectKey = $"{LotFilesEntityType}/{lot.Id}/{Guid.NewGuid():N}-{certificate.CertificateNumber}.pdf";
        using (var stream = new MemoryStream(pdf))
            await storage.UploadAsync(bucket, objectKey, stream, "application/pdf", ct);

        var file = new FileAttachment
        {
            FileName = $"{certificate.CertificateNumber}.pdf",
            ContentType = "application/pdf",
            Size = pdf.Length,
            BucketName = bucket,
            ObjectKey = objectKey,
            EntityType = "lots",
            EntityId = lot.Id,
            UploadedById = certificate.GeneratedById ?? 0,
            DocumentType = "coa",
        };
        db.FileAttachments.Add(file);
        await db.SaveChangesAsync(ct);

        certificate.FileAttachmentId = file.Id;
        await db.SaveChangesAsync(ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
        return certificate;
    }

    private async Task<byte[]> RenderAsync(CertificateOfAnalysis certificate, string language, CancellationToken ct)
    {
        var companyName = await CompanyIdentity.GetCompanyNameAsync(db, ct);
        var generatedBy = certificate.GeneratedById is int uid
            ? await db.Users.AsNoTracking().Where(u => u.Id == uid).Select(u => u.LastName + ", " + u.FirstName).FirstOrDefaultAsync(ct)
            : null;
        return new CertificateOfAnalysisPdfDocument(
            certificate, companyName, generatedBy, CertificateOfAnalysisPdfLabels.For(language)).GeneratePdf();
    }

    private async Task<string> NextNumberAsync(CancellationToken ct)
    {
        var numbers = await db.CertificatesOfAnalysis.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.CertificateNumber.StartsWith(NumberPrefix))
            .Select(c => c.CertificateNumber)
            .ToListAsync(ct);
        var last = numbers
            .Select(n => int.TryParse(n[NumberPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"{NumberPrefix}{(last + 1).ToString("D6", CultureInfo.InvariantCulture)}";
    }

    private async Task<List<LotRecord>> ShippedLotsAsync(int shipmentId, CancellationToken ct)
    {
        var shipped = await db.BinMovements.AsNoTracking()
            .Where(m => m.Reason == BinMovementReason.Ship && m.LotNumber != null
                && db.ShipmentLines.Any(sl => sl.ShipmentId == shipmentId && sl.Id == (m.ShipmentLineId ?? m.EntityId)))
            .Select(m => new { m.LotNumber, LineId = m.ShipmentLineId ?? m.EntityId })
            .Distinct()
            .ToListAsync(ct);
        if (shipped.Count == 0)
            return [];

        var lineParts = await LinePartsAsync(shipmentId, ct);
        var wanted = shipped
            .Where(s => lineParts.ContainsKey(s.LineId))
            .Select(s => (PartId: lineParts[s.LineId], LotNumber: s.LotNumber!))
            .Distinct()
            .ToList();
        var lotNumbers = wanted.Select(w => w.LotNumber).Distinct().ToList();
        var candidates = await db.LotRecords
            .Where(l => lotNumbers.Contains(l.LotNumber))
            .ToListAsync(ct);
        return candidates
            .Where(l => wanted.Contains((l.PartId, l.LotNumber)))
            .OrderBy(l => l.LotNumber)
            .ToList();
    }

    private async Task<List<LotRecord>> LotsOnShipmentPartsAsync(int shipmentId, int lotId, CancellationToken ct)
    {
        var lot = await db.LotRecords.FirstOrDefaultAsync(l => l.Id == lotId, ct)
            ?? throw new KeyNotFoundException($"Lot {lotId} not found.");
        var partIds = (await LinePartsAsync(shipmentId, ct)).Values.ToHashSet();
        if (!partIds.Contains(lot.PartId))
            throw new InvalidOperationException($"Lot {lot.LotNumber} is not for a part on this shipment.");
        return [lot];
    }

    private async Task<Dictionary<int, int>> LinePartsAsync(int shipmentId, CancellationToken ct) =>
        (await db.ShipmentLines.AsNoTracking()
            .Where(sl => sl.ShipmentId == shipmentId)
            .Select(sl => new { sl.Id, PartId = sl.PartId ?? (sl.SalesOrderLine != null ? sl.SalesOrderLine.PartId : null) })
            .ToListAsync(ct))
        .Where(x => x.PartId != null)
        .ToDictionary(x => x.Id, x => x.PartId!.Value);
}
