using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Data.Services;

public class RfqService(AppDbContext db, IClock clock) : IRfqService
{
    public async Task<string> GenerateRfqNumberAsync(CancellationToken ct)
    {
        var today = clock.UtcNow;
        var datePrefix = today.ToString("yyyyMMdd");
        var prefix = $"RFQ-{datePrefix}-";

        var lastNumber = await db.RequestForQuotes
            .AsNoTracking()
            .Where(r => r.RfqNumber.StartsWith(prefix))
            .OrderByDescending(r => r.RfqNumber)
            .Select(r => r.RfqNumber)
            .FirstOrDefaultAsync(ct);

        var sequence = 1;
        if (lastNumber is not null)
        {
            var lastSeq = lastNumber[prefix.Length..];
            if (int.TryParse(lastSeq, out var parsed))
                sequence = parsed + 1;
        }

        return $"{prefix}{sequence:D3}";
    }

    public async Task SendToVendorsAsync(int rfqId, IEnumerable<int> vendorIds, CancellationToken ct)
    {
        var rfq = await db.RequestForQuotes
            .Include(r => r.VendorResponses)
            .FirstOrDefaultAsync(r => r.Id == rfqId, ct)
            ?? throw new KeyNotFoundException($"RFQ {rfqId} not found");

        var now = clock.UtcNow;

        foreach (var vendorId in vendorIds)
        {
            if (rfq.VendorResponses.Any(r => r.VendorId == vendorId))
                continue;

            var vendor = await db.Vendors.FindAsync([vendorId], ct)
                ?? throw new KeyNotFoundException($"Vendor {vendorId} not found");

            rfq.VendorResponses.Add(new RfqVendorResponse
            {
                VendorId = vendorId,
                InvitedAt = now,
                ResponseStatus = RfqResponseStatus.Pending,
            });
        }

        rfq.Status = RfqStatus.Sent;
        rfq.SentAt ??= now;

        await db.SaveChangesAsync(ct);
    }

    public async Task<int> AwardAndCreatePoAsync(int rfqId, int vendorResponseId, CancellationToken ct)
    {
        var rfq = await db.RequestForQuotes
            .Include(r => r.VendorResponses)
            .Include(r => r.Part)
            .FirstOrDefaultAsync(r => r.Id == rfqId, ct)
            ?? throw new KeyNotFoundException($"RFQ {rfqId} not found");

        var response = rfq.VendorResponses.FirstOrDefault(r => r.Id == vendorResponseId)
            ?? throw new KeyNotFoundException($"Vendor response {vendorResponseId} not found on RFQ {rfqId}");

        if (response.ResponseStatus != RfqResponseStatus.Received)
            throw new InvalidOperationException("Can only award a vendor response with status 'Received'");

        var now = clock.UtcNow;

        // Award the selected response
        response.IsAwarded = true;
        response.ResponseStatus = RfqResponseStatus.Awarded;

        // Mark other responses as not awarded
        foreach (var other in rfq.VendorResponses.Where(r => r.Id != vendorResponseId))
        {
            if (other.ResponseStatus == RfqResponseStatus.Received)
                other.ResponseStatus = RfqResponseStatus.NotAwarded;
        }

        // Generate PO number
        var datePrefix = now.ToString("yyyyMMdd");
        var poPrefix = $"PO-{datePrefix}-";
        var lastPo = await db.PurchaseOrders
            .AsNoTracking()
            .Where(p => p.PONumber.StartsWith(poPrefix))
            .OrderByDescending(p => p.PONumber)
            .Select(p => p.PONumber)
            .FirstOrDefaultAsync(ct);

        var seq = 1;
        if (lastPo is not null)
        {
            var lastSeq = lastPo[poPrefix.Length..];
            if (int.TryParse(lastSeq, out var parsed))
                seq = parsed + 1;
        }

        // Create PO from RFQ data
        var po = new PurchaseOrder
        {
            PONumber = $"{poPrefix}{seq:D3}",
            VendorId = response.VendorId,
            Status = PurchaseOrderStatus.Draft,
            Notes = $"Generated from {rfq.RfqNumber}",
            // S4b provenance — quote-driven automation: PO raised by awarding
            // an RFQ vendor response.
            OriginSource = PoOriginSource.AutoQuote,
            OriginReference = rfq.RfqNumber,
        };

        po.Lines.Add(new PurchaseOrderLine
        {
            PartId = rfq.PartId,
            Description = rfq.Part.Description ?? rfq.Part.Name,
            OrderedQuantity = (int)rfq.Quantity,
            UnitPrice = response.UnitPrice ?? 0m,
        });

        db.PurchaseOrders.Add(po);

        // Update RFQ
        rfq.Status = RfqStatus.Awarded;
        rfq.AwardedAt = now;
        rfq.AwardedVendorResponseId = vendorResponseId;

        await db.SaveChangesAsync(ct);

        rfq.GeneratedPurchaseOrderId = po.Id;
        await db.SaveChangesAsync(ct);

        return po.Id;
    }
}
