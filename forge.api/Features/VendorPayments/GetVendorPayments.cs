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

public record GetVendorPaymentByIdQuery(int Id) : IRequest<VendorPaymentListItemModel>;

public class GetVendorPaymentByIdHandler(IVendorPaymentRepository repo, IVendorRepository vendorRepo)
    : IRequestHandler<GetVendorPaymentByIdQuery, VendorPaymentListItemModel>
{
    public async Task<VendorPaymentListItemModel> Handle(GetVendorPaymentByIdQuery request, CancellationToken cancellationToken)
    {
        var payment = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor payment {request.Id} not found");

        var vendor = await vendorRepo.FindAsync(payment.VendorId, cancellationToken);

        return new VendorPaymentListItemModel(
            payment.Id, payment.PaymentNumber, payment.VendorId, vendor?.CompanyName ?? $"Vendor {payment.VendorId}",
            payment.Method.ToString(), payment.Amount, payment.AppliedAmount, payment.UnappliedAmount,
            payment.PaymentDate, payment.ReferenceNumber, payment.CreatedAt);
    }
}
