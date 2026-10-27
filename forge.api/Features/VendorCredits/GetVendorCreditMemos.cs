using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorCredits;

/// <summary>Vendor credit and debit memos, newest first. <c>OpenOnly</c> keeps issued memos with something left to apply.</summary>
public record GetVendorCreditMemosQuery(
    int? VendorId = null,
    VendorCreditMemoStatus? Status = null,
    int? VendorBillId = null,
    bool OpenOnly = false,
    string? Search = null,
    int? CurrencyId = null) : IRequest<List<VendorCreditMemoListItemModel>>;

public class GetVendorCreditMemosHandler(AppDbContext db) : IRequestHandler<GetVendorCreditMemosQuery, List<VendorCreditMemoListItemModel>>
{
    private const int MaxRows = 500;

    public async Task<List<VendorCreditMemoListItemModel>> Handle(GetVendorCreditMemosQuery request, CancellationToken ct)
    {
        var query = db.VendorCreditMemos.AsNoTracking()
            .Include(m => m.Vendor)
            .Include(m => m.Currency)
            .Include(m => m.VendorBill)
            .Include(m => m.VendorReturn)
            .Include(m => m.Lines)
            .Include(m => m.Applications)
            .AsSplitQuery()
            .AsQueryable();

        if (request.VendorId is int vendorId)
            query = query.Where(m => m.VendorId == vendorId);
        if (request.CurrencyId is int currencyId)
            query = query.Where(m => m.CurrencyId == currencyId);
        if (request.Status is VendorCreditMemoStatus status)
            query = query.Where(m => m.Status == status);
        if (request.VendorBillId is int billId)
            query = query.Where(m => m.VendorBillId == billId || m.Applications.Any(a => a.VendorBillId == billId && a.ReversedAt == null));
        if (request.OpenOnly)
            query = query.Where(m => m.Status == VendorCreditMemoStatus.Open || m.Status == VendorCreditMemoStatus.PartiallyApplied);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(m => m.MemoNumber.ToLower().Contains(term)
                || m.Vendor.CompanyName.ToLower().Contains(term)
                || m.Reason.ToLower().Contains(term)
                || (m.VendorReference != null && m.VendorReference.ToLower().Contains(term)));
        }

        var memos = await query
            .OrderByDescending(m => m.MemoDate).ThenByDescending(m => m.Id)
            .Take(MaxRows)
            .ToListAsync(ct);

        return memos
            .Where(m => !request.OpenOnly || m.OpenAmount > 0m)
            .Select(m => new VendorCreditMemoListItemModel(
                m.Id, m.MemoNumber, m.Kind.ToString(), m.VendorId, m.Vendor.CompanyName,
                m.VendorBillId, m.VendorBill?.BillNumber, m.VendorReturnId, m.VendorReturn?.ReturnNumber,
                m.SourceType.ToString(), m.Reason, m.VendorReference, m.Status.ToString(), m.MemoDate,
                m.Total, m.AmountApplied, m.OpenAmount, m.CurrencyId, m.Currency?.Code, m.FxRate, m.CreatedAt))
            .ToList();
    }
}
