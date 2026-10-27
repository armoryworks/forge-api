using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorCredits;

public record GetVendorStatementQuery(int VendorId) : IRequest<VendorStatementModel>;

/// <summary>
/// A vendor's open items: approved bills with a balance due and issued credit or debit memos with an open amount, the
/// memos as negative amounts, oldest first, netted per currency.
/// </summary>
public class GetVendorStatementHandler(AppDbContext db, IClock clock) : IRequestHandler<GetVendorStatementQuery, VendorStatementModel>
{
    public async Task<VendorStatementModel> Handle(GetVendorStatementQuery request, CancellationToken ct)
    {
        var vendor = await db.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == request.VendorId, ct)
            ?? throw new KeyNotFoundException($"Vendor {request.VendorId} not found");

        var bills = await db.VendorBills.AsNoTracking()
            .Include(b => b.Lines)
            .Include(b => b.PaymentApplications)
            .Include(b => b.CreditApplications)
            .Where(b => b.VendorId == vendor.Id && (b.Status == VendorBillStatus.Approved || b.Status == VendorBillStatus.PartiallyPaid))
            .AsSplitQuery()
            .ToListAsync(ct);
        var memos = await db.VendorCreditMemos.AsNoTracking()
            .Include(m => m.Lines)
            .Include(m => m.Applications)
            .Where(m => m.VendorId == vendor.Id
                && (m.Status == VendorCreditMemoStatus.Open || m.Status == VendorCreditMemoStatus.PartiallyApplied))
            .AsSplitQuery()
            .ToListAsync(ct);
        var currencyCodes = await db.Currencies.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Code, ct);

        var lines = bills.Where(b => b.BalanceDue > 0m)
            .Select(b => new VendorStatementLineModel(
                "VendorBill", b.Id, b.BillNumber, b.VendorInvoiceNumber, b.BillDate, b.DueDate,
                b.CurrencyId, currencyCodes.GetValueOrDefault(b.CurrencyId), b.Total, b.BalanceDue))
            .Concat(memos.Where(m => m.OpenAmount > 0m)
                .Select(m => new VendorStatementLineModel(
                    "VendorCreditMemo", m.Id, m.MemoNumber, m.VendorReference, m.MemoDate, null,
                    m.CurrencyId, currencyCodes.GetValueOrDefault(m.CurrencyId), -m.Total, -m.OpenAmount)))
            .OrderBy(l => l.DocumentDate).ThenBy(l => l.DocumentNumber, StringComparer.Ordinal)
            .ToList();

        var totals = lines.GroupBy(l => l.CurrencyId)
            .Select(g => new VendorStatementTotalModel(g.Key, currencyCodes.GetValueOrDefault(g.Key), g.Sum(l => l.OpenAmount)))
            .OrderBy(t => t.CurrencyCode, StringComparer.Ordinal)
            .ToList();

        return new VendorStatementModel(vendor.Id, vendor.CompanyName, clock.UtcNow, lines, totals);
    }
}
