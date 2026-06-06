using MediatR;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.VendorBills;

public record GetVendorBillsQuery(int? VendorId, VendorBillStatus? Status)
    : IRequest<List<VendorBillListItemModel>>;

public class GetVendorBillsHandler(IVendorBillRepository repo)
    : IRequestHandler<GetVendorBillsQuery, List<VendorBillListItemModel>>
{
    public Task<List<VendorBillListItemModel>> Handle(GetVendorBillsQuery request, CancellationToken cancellationToken)
        => repo.GetAllAsync(request.VendorId, request.Status, cancellationToken);
}

public record GetVendorBillByIdQuery(int Id) : IRequest<VendorBillListItemModel>;

public class GetVendorBillByIdHandler(IVendorBillRepository repo, IVendorRepository vendorRepo)
    : IRequestHandler<GetVendorBillByIdQuery, VendorBillListItemModel>
{
    public async Task<VendorBillListItemModel> Handle(GetVendorBillByIdQuery request, CancellationToken cancellationToken)
    {
        var bill = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor bill {request.Id} not found");

        var vendor = await vendorRepo.FindAsync(bill.VendorId, cancellationToken);

        return new VendorBillListItemModel(
            bill.Id, bill.BillNumber, bill.VendorId, vendor?.CompanyName ?? $"Vendor {bill.VendorId}",
            bill.VendorInvoiceNumber, bill.Status.ToString(), bill.BillDate, bill.DueDate,
            bill.Total, bill.AmountPaid, bill.BalanceDue, bill.CreatedAt);
    }
}
