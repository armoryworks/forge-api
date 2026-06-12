using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

/// <summary>⚡ ACCOUNTING BOUNDARY — VendorBill repository (AP counterpart of <see cref="InvoiceRepository"/>).</summary>
public class VendorBillRepository(AppDbContext db) : IVendorBillRepository
{
    public async Task<List<VendorBillListItemModel>> GetAllAsync(int? vendorId, VendorBillStatus? status, CancellationToken ct)
    {
        var query = db.VendorBills
            .Include(b => b.Lines)
            .Include(b => b.PaymentApplications)
            .AsQueryable();

        if (vendorId is int vid)
            query = query.Where(b => b.VendorId == vid);
        if (status is VendorBillStatus st)
            query = query.Where(b => b.Status == st);

        var bills = await query.OrderByDescending(b => b.Id).ToListAsync(ct);

        var vendorIds = bills.Select(b => b.VendorId).Distinct().ToList();
        var vendorNames = await db.Set<Vendor>()
            .Where(v => vendorIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.CompanyName, ct);

        // Failed-transmission flag: one pre-fetched latest-transmission-per-payment dictionary
        // over every payment applied to any listed bill (no N+1).
        var paymentIds = bills
            .SelectMany(b => b.PaymentApplications.Select(a => a.VendorPaymentId))
            .Distinct()
            .ToList();
        var failedPaymentIds = await GetFailedLatestTransmissionPaymentIdsAsync(paymentIds, ct);

        return bills.Select(b => new VendorBillListItemModel(
            b.Id, b.BillNumber, b.VendorId,
            vendorNames.TryGetValue(b.VendorId, out var n) ? n : $"Vendor {b.VendorId}",
            b.VendorInvoiceNumber, b.Status.ToString(), b.BillDate, b.DueDate,
            b.Total, b.AmountPaid, b.BalanceDue, b.CreatedAt,
            b.PaymentApplications.Any(a => failedPaymentIds.Contains(a.VendorPaymentId)),
            b.ExpenseId)).ToList();
    }

    public async Task<bool> HasFailedTransmissionAsync(IReadOnlyCollection<int> vendorPaymentIds, CancellationToken ct)
    {
        if (vendorPaymentIds.Count == 0) return false;
        var failed = await GetFailedLatestTransmissionPaymentIdsAsync(vendorPaymentIds, ct);
        return failed.Count > 0;
    }

    /// <summary>Payment ids (from <paramref name="paymentIds"/>) whose LATEST transmission is Failed.</summary>
    private async Task<HashSet<int>> GetFailedLatestTransmissionPaymentIdsAsync(
        IReadOnlyCollection<int> paymentIds, CancellationToken ct)
    {
        if (paymentIds.Count == 0) return [];

        return (await db.PaymentTransmissions.AsNoTracking()
                .Where(t => t.SourceType == "VendorPayment" && paymentIds.Contains(t.SourceId))
                .OrderByDescending(t => t.Id)
                .ToListAsync(ct))
            .GroupBy(t => t.SourceId)
            .Where(g => g.First().Status == PaymentTransmissionStatus.Failed)
            .Select(g => g.Key)
            .ToHashSet();
    }

    public Task<VendorBill?> FindAsync(int id, CancellationToken ct)
        => db.VendorBills.FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<bool> ExistsForVendorInvoiceAsync(int vendorId, string vendorInvoiceNumber, CancellationToken ct)
        => db.VendorBills.AnyAsync(b => b.VendorId == vendorId && b.VendorInvoiceNumber == vendorInvoiceNumber, ct);

    public Task<VendorBill?> FindWithDetailsAsync(int id, CancellationToken ct)
        => db.VendorBills
            .Include(b => b.Lines).ThenInclude(l => l.PurchaseOrderLine)
            .Include(b => b.PaymentApplications)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task<string> GenerateNextBillNumberAsync(CancellationToken ct)
    {
        var last = await db.VendorBills
            .IgnoreQueryFilters()
            .OrderByDescending(b => b.Id)
            .Select(b => b.BillNumber)
            .FirstOrDefaultAsync(ct);

        if (last != null && last.StartsWith("BILL-") && int.TryParse(last[5..], out var lastNum))
            return $"BILL-{lastNum + 1:D5}";

        return "BILL-00001";
    }

    public async Task AddAsync(VendorBill bill, CancellationToken ct) => await db.VendorBills.AddAsync(bill, ct);

    public async Task SaveChangesAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
}
