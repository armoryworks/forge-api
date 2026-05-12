using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Vendors;

public record GetVendorByIdQuery(int Id) : IRequest<VendorDetailResponseModel>;

public class GetVendorByIdHandler(IVendorRepository repo)
    : IRequestHandler<GetVendorByIdQuery, VendorDetailResponseModel>
{
    public async Task<VendorDetailResponseModel> Handle(GetVendorByIdQuery request, CancellationToken cancellationToken)
    {
        var vendor = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor {request.Id} not found");

        return new VendorDetailResponseModel(
            vendor.Id,
            vendor.CompanyName,
            vendor.ContactName,
            vendor.Email,
            vendor.Phone,
            vendor.Address,
            vendor.City,
            vendor.State,
            vendor.ZipCode,
            vendor.Country,
            vendor.PaymentTerms,
            vendor.Notes,
            vendor.IsActive,
            vendor.ExternalId,
            vendor.CreatedAt,
            vendor.UpdatedAt,
            vendor.PurchaseOrders.Select(po => new PurchaseOrderListItemModel(
                po.Id,
                po.PONumber,
                po.VendorId,
                vendor.CompanyName,
                po.JobId,
                po.Job?.JobNumber,
                po.Status.ToString(),
                po.Lines.Count,
                po.Lines.Sum(l => l.OrderedQuantity),
                po.Lines.Sum(l => l.ReceivedQuantity),
                po.ExpectedDeliveryDate,
                po.IsBlanket,
                po.CreatedAt)).ToList());
    }
}
