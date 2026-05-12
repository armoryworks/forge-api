using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerReturns;

public record GetCustomerReturnsQuery(int? CustomerId, CustomerReturnStatus? Status) : IRequest<List<CustomerReturnListItemModel>>;

public class GetCustomerReturnsHandler(AppDbContext db)
    : IRequestHandler<GetCustomerReturnsQuery, List<CustomerReturnListItemModel>>
{
    public async Task<List<CustomerReturnListItemModel>> Handle(GetCustomerReturnsQuery request, CancellationToken ct)
    {
        var query = db.CustomerReturns
            .Include(r => r.Customer)
            .Include(r => r.OriginalJob)
            .Include(r => r.ReworkJob)
            .AsQueryable();

        if (request.CustomerId.HasValue)
            query = query.Where(r => r.CustomerId == request.CustomerId.Value);
        if (request.Status.HasValue)
            query = query.Where(r => r.Status == request.Status.Value);

        return await query
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new CustomerReturnListItemModel(
                r.Id, r.ReturnNumber, r.CustomerId, r.Customer.Name,
                r.OriginalJobId, r.OriginalJob.JobNumber,
                r.ReworkJobId, r.ReworkJob != null ? r.ReworkJob.JobNumber : null,
                r.Status.ToString(), r.Reason, r.ReturnDate, r.CreatedAt))
            .ToListAsync(ct);
    }
}
