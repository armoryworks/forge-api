using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.PaymentTerms;
using Forge.Api.Services.Fx;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Vendors;

public record GetVendorByIdQuery(int Id) : IRequest<VendorDetailResponseModel>;

public class GetVendorByIdHandler(IVendorRepository repo, AppDbContext? db = null)
    : IRequestHandler<GetVendorByIdQuery, VendorDetailResponseModel>
{
    public async Task<VendorDetailResponseModel> Handle(GetVendorByIdQuery request, CancellationToken cancellationToken)
    {
        var vendor = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor {request.Id} not found");

        return new VendorDetailResponseModel(
            vendor.Id,
            vendor.CompanyName,
            vendor.VendorNumber,
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
            vendor.OffTierVariancePct,
            vendor.IsActive,
            vendor.Is1099,
            vendor.TaxId,
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
                po.CreatedAt,
                LinesFullyReceived: po.Lines.Count(l => l.ReceivedQuantity + l.CancelledShortCloseQuantity >= l.OrderedQuantity))).ToList(),
            vendor.Fax,
            vendor.Version,
            vendor.CurrencyId,
            db is not null && vendor.CurrencyId is int currencyId
                ? await db.Currencies.AsNoTracking().Where(c => c.Id == currencyId).Select(c => c.Code).FirstOrDefaultAsync(cancellationToken)
                : null,
            db is not null && await PartyCurrency.VendorHasPostedDocumentsAsync(db, vendor.Id, cancellationToken),
            vendor.PaymentTermsId,
            db is null ? null : await new PaymentTermsResolver(db).ModelAsync(vendor.PaymentTermsId, null, cancellationToken),
            vendor.AutoPoMode?.ToString(),
            vendor.OverReceiptTolerancePct,
            vendor.UnderReceiptTolerancePct);
    }
}
