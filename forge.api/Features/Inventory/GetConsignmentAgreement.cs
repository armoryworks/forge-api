using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record GetConsignmentAgreementQuery(int Id) : IRequest<ConsignmentAgreementResponseModel>;

public class GetConsignmentAgreementHandler(AppDbContext db) : IRequestHandler<GetConsignmentAgreementQuery, ConsignmentAgreementResponseModel>
{
    public async Task<ConsignmentAgreementResponseModel> Handle(GetConsignmentAgreementQuery request, CancellationToken cancellationToken)
    {
        var agreement = await db.ConsignmentAgreements
            .AsNoTracking()
            .Include(a => a.Vendor)
            .Include(a => a.Customer)
            .Include(a => a.Part)
            .Include(a => a.Transactions)
            .Where(a => a.Id == request.Id)
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
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Consignment agreement {request.Id} not found");

        return agreement;
    }
}
