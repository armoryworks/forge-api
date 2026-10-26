using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerReturns;

public record GetCustomerReturnByIdQuery(int Id) : IRequest<CustomerReturnDetailResponseModel>;

public class GetCustomerReturnByIdHandler(AppDbContext db)
    : IRequestHandler<GetCustomerReturnByIdQuery, CustomerReturnDetailResponseModel>
{
    public async Task<CustomerReturnDetailResponseModel> Handle(GetCustomerReturnByIdQuery request, CancellationToken ct)
    {
        var r = await db.CustomerReturns
            .Include(r => r.Customer)
            .Include(r => r.OriginalJob)
            .Include(r => r.Channel)
            .Include(r => r.ReworkJob)
            .Include(r => r.SalesOrderLine)
            .Include(r => r.Invoice)
            .Include(r => r.InvoiceLine)
            .Include(r => r.RestockLocation)
            .Include(r => r.CreditMemo)
            .FirstOrDefaultAsync(r => r.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Customer return {request.Id} not found");

        var partId = ReturnDispositionService.PartIdFor(r);
        var part = partId is int id
            ? await db.Parts.AsNoTracking().Where(p => p.Id == id).Select(p => new { p.PartNumber, p.TraceabilityType }).FirstOrDefaultAsync(ct)
            : null;

        string? inspectedByName = null;
        if (r.InspectedById.HasValue)
        {
            var user = await db.Users.FindAsync([r.InspectedById.Value], ct);
            inspectedByName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : null;
        }

        return new CustomerReturnDetailResponseModel(
            r.Id, r.ReturnNumber, r.CustomerId, r.Customer.Name,
            r.OriginalJobId, r.OriginalJob?.JobNumber, r.OriginalJob?.Title,
            r.ReworkJobId, r.ReworkJob?.JobNumber,
            r.Status.ToString(), r.Reason, r.Notes, r.ReturnDate,
            r.InspectedById, inspectedByName, r.InspectedAt, r.InspectionNotes,
            r.CreatedAt, r.UpdatedAt,
            r.ChannelId, r.Channel?.Name, r.ExternalRmaId,
            r.RefundAmount, r.Quantity, r.SalesOrderLineId,
            r.InvoiceId, r.Invoice?.InvoiceNumber, r.Disposition?.ToString(),
            r.QuantityRestocked, r.QuantityScrapped, r.RestockLocationId, r.RestockLocation?.Name, r.RestockLotNumber,
            r.RestockBinMovementId, r.CreditMemoId, r.CreditMemo?.CreditMemoNumber,
            partId, part?.PartNumber, part?.TraceabilityType == TraceabilityType.Lot);
    }
}
