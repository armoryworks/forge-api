using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

/// <summary>⚡ ACCOUNTING BOUNDARY — VendorPayment repository (AP counterpart of <see cref="PaymentRepository"/>).</summary>
public class VendorPaymentRepository(AppDbContext db) : IVendorPaymentRepository
{
    public async Task<List<VendorPaymentListItemModel>> GetAllAsync(int? vendorId, CancellationToken ct)
    {
        var query = db.VendorPayments.Include(p => p.Applications).AsQueryable();

        if (vendorId is int vid)
            query = query.Where(p => p.VendorId == vid);

        var payments = await query.OrderByDescending(p => p.Id).ToListAsync(ct);

        var vendorIds = payments.Select(p => p.VendorId).Distinct().ToList();
        var vendorNames = await db.Set<Vendor>()
            .Where(v => vendorIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.CompanyName, ct);

        // Latest bank transmission per payment in ONE pre-fetched dictionary (no N+1):
        // ordered by Id desc, so the first row per source is the latest.
        var paymentIds = payments.Select(p => p.Id).ToList();
        var latestTransmissions = (await db.PaymentTransmissions.AsNoTracking()
                .Where(t => t.SourceType == "VendorPayment" && paymentIds.Contains(t.SourceId))
                .OrderByDescending(t => t.Id)
                .ToListAsync(ct))
            .GroupBy(t => t.SourceId)
            .ToDictionary(g => g.Key, g => g.First());

        return payments.Select(p =>
        {
            latestTransmissions.TryGetValue(p.Id, out var tx);
            return new VendorPaymentListItemModel(
                p.Id, p.PaymentNumber, p.VendorId,
                vendorNames.TryGetValue(p.VendorId, out var n) ? n : $"Vendor {p.VendorId}",
                p.Method.ToString(), p.Amount, p.AppliedAmount, p.UnappliedAmount,
                p.PaymentDate, p.ReferenceNumber, p.CreatedAt,
                tx?.Status.ToString(), tx?.AttemptCount ?? 0, tx?.Id);
        }).ToList();
    }

    public Task<PaymentTransmission?> FindLatestTransmissionAsync(int paymentId, CancellationToken ct)
        => db.PaymentTransmissions.AsNoTracking()
            .Where(t => t.SourceType == "VendorPayment" && t.SourceId == paymentId)
            .OrderByDescending(t => t.Id)
            .FirstOrDefaultAsync(ct);

    public Task<VendorPayment?> FindAsync(int id, CancellationToken ct)
        => db.VendorPayments.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<VendorPayment?> FindWithDetailsAsync(int id, CancellationToken ct)
        => db.VendorPayments
            // VendorBill nav supplies BillNumber for the payment-detail application breakdown.
            .Include(p => p.Applications).ThenInclude(a => a.VendorBill)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<string> GenerateNextVendorPaymentNumberAsync(CancellationToken ct)
    {
        var last = await db.VendorPayments
            .IgnoreQueryFilters()
            .OrderByDescending(p => p.Id)
            .Select(p => p.PaymentNumber)
            .FirstOrDefaultAsync(ct);

        if (last != null && last.StartsWith("VPMT-") && int.TryParse(last[5..], out var lastNum))
            return $"VPMT-{lastNum + 1:D5}";

        return "VPMT-00001";
    }

    public async Task AddAsync(VendorPayment payment, CancellationToken ct) => await db.VendorPayments.AddAsync(payment, ct);

    public async Task SaveChangesAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
}
