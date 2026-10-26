using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorBills;

public record GetVendorBillsQuery(int? VendorId, VendorBillStatus? Status)
    : IRequest<List<VendorBillListItemModel>>;

public class GetVendorBillsHandler(IVendorBillRepository repo)
    : IRequestHandler<GetVendorBillsQuery, List<VendorBillListItemModel>>
{
    public Task<List<VendorBillListItemModel>> Handle(GetVendorBillsQuery request, CancellationToken cancellationToken)
        => repo.GetAllAsync(request.VendorId, request.Status, cancellationToken);
}

public record GetVendorBillByIdQuery(int Id) : IRequest<VendorBillDetailModel>;

public class GetVendorBillByIdHandler(IVendorBillRepository repo, IVendorRepository vendorRepo, AppDbContext? db = null)
    : IRequestHandler<GetVendorBillByIdQuery, VendorBillDetailModel>
{
    public async Task<VendorBillDetailModel> Handle(GetVendorBillByIdQuery request, CancellationToken cancellationToken)
    {
        var bill = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor bill {request.Id} not found");

        var vendor = await vendorRepo.FindAsync(bill.VendorId, cancellationToken);

        // Failed-transmission flag over the bill's applied payments (single grouped lookup, no N+1).
        var hasFailedTransmission = await repo.HasFailedTransmissionAsync(
            bill.PaymentApplications.Select(a => a.VendorPaymentId).Distinct().ToList(), cancellationToken);

        var poIds = bill.Lines
            .Select(l => l.PurchaseOrderLine?.PurchaseOrderId)
            .Append(bill.PurchaseOrderId)
            .Concat(bill.Lines.Select(l => l.FreightForPurchaseOrderId))
            .OfType<int>()
            .Distinct()
            .ToList();
        var poRefs = await repo.GetPurchaseOrderRefsAsync(poIds, cancellationToken);

        return new VendorBillDetailModel(
            bill.Id, bill.BillNumber, bill.VendorId, vendor?.CompanyName ?? $"Vendor {bill.VendorId}",
            bill.VendorInvoiceNumber, bill.PurchaseOrderId, bill.Status.ToString(), bill.BillDate, bill.DueDate,
            bill.Subtotal, bill.TaxAmount, bill.Total, bill.AmountPaid, bill.BalanceDue,
            bill.CurrencyId, bill.FxRate, bill.Notes, bill.CreatedAt,
            bill.Lines
                .OrderBy(l => l.LineNumber)
                .Select(l => new VendorBillLineDetailModel(
                    l.Id, l.LineNumber, l.PartId, l.PurchaseOrderLineId,
                    l.Description, l.Quantity, l.UnitPrice, l.LineTotal, l.AccountDeterminationKey,
                    l.GlAccountId,
                    l.GlAccount is null ? null : $"{l.GlAccount.AccountNumber} {l.GlAccount.Name}",
                    l.PurchaseOrderLineId is int lineId && poRefs.Lines.TryGetValue(lineId, out var lineRef)
                        ? $"{lineRef.PoNumber} line {lineRef.Position}"
                        : null,
                    l.LineType,
                    l.FreightForPurchaseOrderId,
                    l.FreightForPurchaseOrderId is int freightPoId && poRefs.PoNumbers.TryGetValue(freightPoId, out var freightPoNumber)
                        ? freightPoNumber
                        : null,
                    l.FreightClearedAmount))
                .ToList(),
            hasFailedTransmission,
            bill.ExpenseId,
            bill.PurchaseOrderId is int poId && poRefs.PoNumbers.TryGetValue(poId, out var poNumber)
                ? poNumber
                : null,
            db is null
                ? bill.Currency?.Code
                : await db.Currencies.AsNoTracking()
                    .Where(c => c.Id == bill.CurrencyId).Select(c => c.Code).FirstOrDefaultAsync(cancellationToken));
    }
}
