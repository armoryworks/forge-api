using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record GetConsignmentStockSummaryQuery(int? VendorId, int? CustomerId) : IRequest<ConsignmentStockSummaryResponseModel>;

public class GetConsignmentStockSummaryHandler(AppDbContext db) : IRequestHandler<GetConsignmentStockSummaryQuery, ConsignmentStockSummaryResponseModel>
{
    public async Task<ConsignmentStockSummaryResponseModel> Handle(GetConsignmentStockSummaryQuery request, CancellationToken cancellationToken)
    {
        var query = db.ConsignmentAgreements.AsNoTracking().AsQueryable();

        if (request.VendorId.HasValue)
            query = query.Where(a => a.VendorId == request.VendorId.Value);

        if (request.CustomerId.HasValue)
            query = query.Where(a => a.CustomerId == request.CustomerId.Value);

        var agreements = await query
            .Include(a => a.Vendor)
            .Include(a => a.Customer)
            .Include(a => a.Transactions)
            .ToListAsync(cancellationToken);

        var byOwner = agreements
            .GroupBy(a => a.Direction == ConsignmentDirection.Inbound
                ? new { OwnerType = "Vendor", OwnerId = a.VendorId ?? 0, OwnerName = a.Vendor?.CompanyName ?? "" }
                : new { OwnerType = "Customer", OwnerId = a.CustomerId ?? 0, OwnerName = a.Customer?.Name ?? "" })
            .Select(g => new ConsignmentStockByOwnerResponseModel
            {
                OwnerType = g.Key.OwnerType,
                OwnerId = g.Key.OwnerId,
                OwnerName = g.Key.OwnerName,
                AgreementCount = g.Count(),
                TotalQuantity = g.Sum(a => a.Transactions.Sum(t => t.Quantity)),
                TotalValue = g.Sum(a => a.Transactions.Sum(t => t.ExtendedAmount)),
            })
            .ToList();

        return new ConsignmentStockSummaryResponseModel
        {
            TotalAgreements = agreements.Count,
            ActiveAgreements = agreements.Count(a => a.Status == ConsignmentAgreementStatus.Active),
            TotalConsignedValue = byOwner.Sum(o => o.TotalValue),
            ByOwner = byOwner,
        };
    }
}
