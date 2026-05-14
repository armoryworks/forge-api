using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record GetConsignmentAgreementsQuery(
    int? VendorId,
    int? CustomerId,
    ConsignmentAgreementStatus? Status,
    int? PartId) : IRequest<List<ConsignmentAgreementResponseModel>>;

public class GetConsignmentAgreementsHandler(AppDbContext db) : IRequestHandler<GetConsignmentAgreementsQuery, List<ConsignmentAgreementResponseModel>>
{
    public async Task<List<ConsignmentAgreementResponseModel>> Handle(GetConsignmentAgreementsQuery request, CancellationToken cancellationToken)
    {
        var query = db.ConsignmentAgreements
            .AsNoTracking()
            .Include(a => a.Vendor)
            .Include(a => a.Customer)
            .Include(a => a.Part)
            .AsQueryable();

        if (request.VendorId.HasValue)
            query = query.Where(a => a.VendorId == request.VendorId.Value);

        if (request.CustomerId.HasValue)
            query = query.Where(a => a.CustomerId == request.CustomerId.Value);

        if (request.Status.HasValue)
            query = query.Where(a => a.Status == request.Status.Value);

        if (request.PartId.HasValue)
            query = query.Where(a => a.PartId == request.PartId.Value);

        return await query
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new ConsignmentAgreementResponseModel
            {
                Id = a.Id,
                Direction = a.Direction,
                VendorId = a.VendorId,
                VendorName = a.Vendor != null ? a.Vendor.CompanyName : null,
                CustomerId = a.CustomerId,
                CustomerName = a.Customer != null ? a.Customer.Name : null,
                PartId = a.PartId,
                PartNumber = a.Part.PartNumber,
                PartDescription = a.Part.Description ?? a.Part.Name,
                AgreedUnitPrice = a.AgreedUnitPrice,
                MinStockQuantity = a.MinStockQuantity,
                MaxStockQuantity = a.MaxStockQuantity,
                EffectiveFrom = a.EffectiveFrom,
                EffectiveTo = a.EffectiveTo,
                InvoiceOnConsumption = a.InvoiceOnConsumption,
                Status = a.Status,
                Terms = a.Terms,
                ReconciliationFrequencyDays = a.ReconciliationFrequencyDays,
                TransactionCount = a.Transactions.Count,
                CreatedAt = a.CreatedAt,
            })
            .ToListAsync(cancellationToken);
    }
}
