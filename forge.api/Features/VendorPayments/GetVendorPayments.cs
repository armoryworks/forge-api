using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.VendorPayments;

public record GetVendorPaymentsQuery(int? VendorId)
    : IRequest<List<VendorPaymentListItemModel>>;

public class GetVendorPaymentsHandler(IVendorPaymentRepository repo)
    : IRequestHandler<GetVendorPaymentsQuery, List<VendorPaymentListItemModel>>
{
    public Task<List<VendorPaymentListItemModel>> Handle(GetVendorPaymentsQuery request, CancellationToken cancellationToken)
        => repo.GetAllAsync(request.VendorId, cancellationToken);
}

public record GetVendorPaymentByIdQuery(int Id) : IRequest<VendorPaymentDetailModel>;

public class GetVendorPaymentByIdHandler(IVendorPaymentRepository repo, IVendorRepository vendorRepo)
    : IRequestHandler<GetVendorPaymentByIdQuery, VendorPaymentDetailModel>
{
    public async Task<VendorPaymentDetailModel> Handle(GetVendorPaymentByIdQuery request, CancellationToken cancellationToken)
    {
        var payment = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor payment {request.Id} not found");

        var vendor = await vendorRepo.FindAsync(payment.VendorId, cancellationToken);

        // Latest bank transmission (null for non-electronic payments) — single lookup, no N+1.
        var transmission = await repo.FindLatestTransmissionAsync(payment.Id, cancellationToken);

        return new VendorPaymentDetailModel(
            payment.Id, payment.PaymentNumber, payment.VendorId, vendor?.CompanyName ?? $"Vendor {payment.VendorId}",
            payment.Method.ToString(), payment.Amount, payment.AppliedAmount, payment.UnappliedAmount,
            payment.PaymentDate, payment.ReferenceNumber, payment.Notes, payment.CreatedAt,
            payment.Applications
                .OrderBy(a => a.Id)
                .Select(a => new VendorPaymentApplicationDetailModel(
                    a.VendorBillId,
                    a.VendorBill?.BillNumber ?? $"Bill {a.VendorBillId}",
                    a.Amount,
                    a.SettlementFxRate))
                .ToList(),
            transmission?.Id, transmission?.Status.ToString(), transmission?.AttemptCount ?? 0,
            transmission?.LastError, transmission?.SubmissionRef, transmission?.NextAttemptAt);
    }
}
