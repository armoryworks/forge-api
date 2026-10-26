using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorCredits;

public record GetVendorCreditMemoByIdQuery(int Id) : IRequest<VendorCreditMemoDetailResponseModel>;

public class GetVendorCreditMemoByIdHandler(AppDbContext db) : IRequestHandler<GetVendorCreditMemoByIdQuery, VendorCreditMemoDetailResponseModel>
{
    public async Task<VendorCreditMemoDetailResponseModel> Handle(GetVendorCreditMemoByIdQuery request, CancellationToken ct)
    {
        var memo = await db.VendorCreditMemos.AsNoTracking()
            .Include(m => m.Vendor)
            .Include(m => m.Currency)
            .Include(m => m.VendorBill)
            .Include(m => m.VendorReturn)
            .Include(m => m.Lines).ThenInclude(l => l.Part)
            .Include(m => m.Applications).ThenInclude(a => a.VendorBill)
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Vendor credit memo {request.Id} not found");

        return new VendorCreditMemoDetailResponseModel(
            memo.Id, memo.MemoNumber, memo.Kind.ToString(), memo.VendorId, memo.Vendor.CompanyName,
            memo.VendorBillId, memo.VendorBill?.BillNumber, memo.VendorReturnId, memo.VendorReturn?.ReturnNumber,
            memo.SourceType.ToString(), memo.Reason, memo.VendorReference, memo.Status.ToString(), memo.MemoDate,
            memo.Subtotal, memo.TaxAmount, memo.Total, memo.AmountApplied, memo.OpenAmount,
            memo.CurrencyId, memo.Currency?.Code, memo.FxRate, memo.Notes, memo.IssuedAt, memo.VoidedAt, memo.VoidReason,
            memo.JournalEntryId,
            memo.Lines.OrderBy(l => l.LineNumber)
                .Select(l => new VendorCreditMemoLineModel(
                    l.Id, l.LineNumber, l.VendorBillLineId, l.VendorReturnLineId, l.PartId, l.Part?.PartNumber,
                    l.Description, l.Quantity, l.UnitPrice, l.LineTotal, l.AccountDeterminationKey, l.GlAccountId))
                .ToList(),
            memo.Applications.OrderBy(a => a.AppliedAt)
                .Select(a => new VendorCreditMemoApplicationModel(
                    a.Id, memo.Id, memo.MemoNumber, a.VendorBillId, a.VendorBill?.BillNumber, a.Amount, a.AppliedAt,
                    a.ReversedAt, a.VendorPaymentId, a.PaymentRunId))
                .ToList(),
            memo.CreatedAt, memo.UpdatedAt, memo.Version);
    }
}
