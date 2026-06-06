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

        return bills.Select(b => new VendorBillListItemModel(
            b.Id, b.BillNumber, b.VendorId,
            vendorNames.TryGetValue(b.VendorId, out var n) ? n : $"Vendor {b.VendorId}",
            b.VendorInvoiceNumber, b.Status.ToString(), b.BillDate, b.DueDate,
            b.Total, b.AmountPaid, b.BalanceDue, b.CreatedAt)).ToList();
    }

    public Task<VendorBill?> FindAsync(int id, CancellationToken ct)
        => db.VendorBills.FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<VendorBill?> FindWithDetailsAsync(int id, CancellationToken ct)
        => db.VendorBills
            .Include(b => b.Lines)
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
